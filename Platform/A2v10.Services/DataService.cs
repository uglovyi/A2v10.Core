﻿// Copyright © 2015-2023 Oleksandr Kukhtin. All rights reserved.

using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using System.IO;
using System.Reflection;

using Newtonsoft.Json;

using A2v10.Data.Interfaces;
using A2v10.Services.Interop;

namespace A2v10.Services;

public record DataLoadResult(IDataModel? Model, IModelView View) : IDataLoadResult;

public class LayoutDescription : ILayoutDescription
{
	public LayoutDescription(List<String>? styles, List<String>? scripts)
	{
		if (styles != null && styles.Count > 0)
		{
			var sb = new StringBuilder();
			foreach (var s in styles)
				sb.Append($"<link href=\"{s}\" rel=\"stylesheet\" />\n");
			ModelStyles = sb.ToString();
		}
		if (scripts != null && scripts.Count > 0)
		{
			var sb = new StringBuilder();
			foreach (var s in scripts)
				sb.Append($"<script type=\"text/javascript\" src=\"{s}\"></script>\n");
			ModelScripts = sb.ToString();
		}
	}

	public String? ModelScripts { get; init; }
	public String? ModelStyles { get; init; }
}

public class SaveResult : ISaveResult
{
	public String Data { get; init; } = "{}";
	public ISignalResult? SignalResult { get; init; }

}
public class DataService(IServiceProvider serviceProvider, IModelJsonReader modelReader, IDbContext dbContext, ICurrentUser currentUser,
    ISqlQueryTextProvider sqlQueryTextProvider, IAppCodeProvider codeProvider) : IDataService
{
	private readonly IServiceProvider _serviceProvider = serviceProvider;
	private readonly IModelJsonReader _modelReader = modelReader;
	private readonly IDbContext _dbContext = dbContext;
	private readonly ICurrentUser _currentUser = currentUser;
	private readonly ISqlQueryTextProvider _sqlQueryTextProvider = sqlQueryTextProvider;
	private readonly IAppCodeProvider _codeProvider = codeProvider;

    static PlatformUrl CreatePlatformUrl(UrlKind kind, String baseUrl)
	{
		return new PlatformUrl(kind, baseUrl, null);
	}

	static PlatformUrl CreatePlatformUrl(String baseUrl, String? id = null)
	{
		return new PlatformUrl(baseUrl, id);
	}

	public Task<IDataLoadResult> LoadAsync(UrlKind kind, String baseUrl, Action<ExpandoObject> setParams)
	{
		var platformBaseUrl = CreatePlatformUrl(kind, baseUrl);
		return Load(platformBaseUrl, setParams);
	}

	public Task<IDataLoadResult> LoadAsync(String baseUrl, Action<ExpandoObject> setParams)
	{
		// with redirect here only!
		var platformBaseUrl = CreatePlatformUrl(baseUrl, null);
		return Load(platformBaseUrl, setParams);
	}

    public async Task<IInvokeResult> ExportAsync(String baseUrl, Action<ExpandoObject> setParams)
    {
        var platformUrl = CreatePlatformUrl(UrlKind.Page, baseUrl);
        var view = await _modelReader.GetViewAsync(platformUrl);
        if (view.Export == null)
			throw new DataServiceException("Export not found");
        var loadPrms = view.CreateParameters(platformUrl, null, setParams);
        var dm = await _dbContext.LoadModelAsync(view.DataSource, view.LoadProcedure(), loadPrms);
		var export = view.Export;
        Stream? stream;
        var templExpr = export.GetTemplateExpression();
        if (!String.IsNullOrEmpty(templExpr))
        {
            var bytes = dm.Eval<Byte[]>(templExpr)
                ?? throw new DataServiceException($"Template stream not found or its format is invalid. ({templExpr})");
            stream = new MemoryStream(bytes);
        }
        else if (!String.IsNullOrEmpty(export.Template))
        {
            var fileName = export.Template.AddExtension(export.Format.ToString());
            var pathToRead = _codeProvider.MakePath(view.Path, fileName);
            stream = _codeProvider.FileStreamRO(pathToRead) ??
                throw new DataServiceException($"Template file not found ({fileName})");
        }
        else
			throw new DataServiceException($"Export template not defined");
		var resultFileName = $"{export.FileName}.{export.Format}";
		var resultMime = MimeTypes.GetMimeMapping($".{export.Format}");

        switch (export.Format)
		{
			case ModelJsonExportFormat.xlsx:
				{
					var rep = new ExcelReportGenerator(stream);
					rep.GenerateReport(dm);
					if (rep.ResultFile == null)
						throw new DataServiceException("Generate file error");
					var bytes = await File.ReadAllBytesAsync(rep.ResultFile);
					return new InvokeResult(bytes, resultMime, resultFileName);
                }
			default:
                throw new DataServiceException($"Export not implemented for {export.Format}");
        }
    }

    private void CheckRoles(IModelBase modelBase)
	{
		if (!modelBase.CheckRoles(_currentUser.Identity.Roles))
			throw new DataServiceException("Access denied");
	}

	async Task<IModelView> LoadViewAsync(IPlatformUrl platformUrl)
	{
		var view = await _modelReader.GetViewAsync(platformUrl);
		CheckRoles(view);
		return view;
	}

	async Task<IDataLoadResult> Load(IPlatformUrl platformUrl, Action<ExpandoObject> setParams)
	{
		var view = await LoadViewAsync(platformUrl);

		var loadPrms = view.CreateParameters(platformUrl, null, setParams);

		IDataModel? model = null;

		if (view.HasModel())
		{
			ExpandoObject prmsForLoad = loadPrms;

			if (view.Indirect)
				prmsForLoad = ParameterBuilder.BuildIndirectParams(platformUrl, setParams);

			var sqlTextKey = view.SqlTextKey();
			if (sqlTextKey == null)
				model = await _dbContext.LoadModelAsync(view.DataSource, view.LoadProcedure(), prmsForLoad);
			else
			{
				var sqlText = _sqlQueryTextProvider.GetSqlText(sqlTextKey, prmsForLoad);
				model = await _dbContext.LoadModelSqlAsync(view.DataSource, sqlText, prmsForLoad);
			}

            if (view.Merge != null)
			{
				var prmsForMerge = view.Merge.CreateMergeParameters(model, prmsForLoad);
				var mergeModel = await _dbContext.LoadModelAsync(view.Merge.DataSource, view.Merge.LoadProcedure(), prmsForMerge);
				model.Merge(mergeModel);
			}

			if (view.Copy)
				model.MakeCopy();

			if (platformUrl.Id != null && !view.Copy)
			{
				// check Main Element
				var me = model.MainElement;
				if (me.Metadata != null)
				{
					var modelId = me.Id ?? String.Empty;
					if (platformUrl.Id != modelId.ToString())
						throw new DataServiceException($"Main element not found. Id={platformUrl.Id}");
				}
			}
		}
		if (view.Indirect)
			view = await LoadIndirect(view, model, setParams);

		if (model != null)
		{
			view = view.Resolve(model);
			SetReadOnly(model);
		}

		return new DataLoadResult
		(
			Model: model,
			View: view
		);
	}

	public Task<String> ExpandAsync(ExpandoObject queryData, Action<ExpandoObject> setParams)
	{
		var baseUrl = queryData.Get<String>("baseUrl") 
			?? throw new DataServiceException(nameof(ExpandAsync));
		Object? id = queryData.Get<Object>("id");
		return ExpandAsync(baseUrl, id, setParams);
	}

	public async Task<String> ExpandAsync(String baseUrl, Object? Id, Action<ExpandoObject> setParams)
	{
		var platformBaseUrl = CreatePlatformUrl(baseUrl);
		var view = await LoadViewAsync(platformBaseUrl);
		var expandProc = view.ExpandProcedure();

		var execPrms = view.CreateParameters(platformBaseUrl, Id, setParams);
		execPrms.SetNotNull("Id", Id);

		var model = await _dbContext.LoadModelAsync(view.DataSource, expandProc, execPrms);
		return JsonConvert.SerializeObject(model.Root, JsonHelpers.DataSerializerSettings);
	}

	public async Task DbRemoveAsync(String baseUrl, Object Id, String? propertyName, Action<ExpandoObject> setParams)
	{
		var platformBaseUrl = CreatePlatformUrl(baseUrl);
		var view = await LoadViewAsync(platformBaseUrl);
		var deleteProc = view.DeleteProcedure(propertyName);

		var execPrms = view.CreateParameters(platformBaseUrl, Id, setParams);
		execPrms.SetNotNull("Id", Id);

		await _dbContext.ExecuteExpandoAsync(view.DataSource, deleteProc, execPrms);
	}

	public async Task<String> ReloadAsync(String baseUrl, Action<ExpandoObject> setParams)
	{
		var result = await LoadAsync(baseUrl, setParams);
		if (result.Model != null)
			return JsonConvert.SerializeObject(result.Model.Root, JsonHelpers.DataSerializerSettings);
		return "{}";
	}

	public Task<String> LoadLazyAsync(ExpandoObject queryData, Action<ExpandoObject> setParams)
	{
		var baseUrl = queryData.Get<String>("baseUrl") 
			?? throw new DataServiceException(nameof(LoadLazyAsync));
		var id = queryData.Get<Object>("id");
		var prop = queryData.GetNotNull<String>("prop");
		return LoadLazyAsync(baseUrl, id, prop, setParams);
	}

	public async Task<String> LoadLazyAsync(String baseUrl, Object? Id, String propertyName, Action<ExpandoObject> setParams)
	{
		String? strId = Id != null ? Convert.ToString(Id, CultureInfo.InvariantCulture) : null;

		var platformBaseUrl = CreatePlatformUrl(baseUrl, strId);
		var view = await LoadViewAsync(platformBaseUrl);

		String loadProc = view.LoadLazyProcedure(propertyName.ToPascalCase());
		var loadParams = view.CreateParameters(platformBaseUrl, Id, setParams, IModelBase.ParametersFlags.SkipModelJsonParams);

		var model = await _dbContext.LoadModelAsync(view.DataSource, loadProc, loadParams);

		return JsonConvert.SerializeObject(model.Root, JsonHelpers.DataSerializerSettings);
	}

	static void ResolveParams(ExpandoObject prms, ExpandoObject data)
	{
		if (prms == null || data == null)
			return;
		var vals = new Dictionary<String, String?>();
		foreach (var (k, v) in prms)
		{
			if (v != null && v is String strVal && strVal.StartsWith("{{"))
			{
				vals.Add(k, data.Resolve(strVal));
			}
		}
		foreach (var (k, v) in vals)
		{
			prms.Set(k, v);
		}
	}

	public async Task<ISaveResult> SaveAsync(String baseUrl, ExpandoObject data, Action<ExpandoObject> setParams)
	{
		var platformBaseUrl = CreatePlatformUrl(baseUrl);
		var view = await LoadViewAsync(platformBaseUrl);

		var savePrms = view.CreateParameters(platformBaseUrl, null, setParams);

		ResolveParams(savePrms, data);

		CheckUserState();

		// TODO: HookHandler, invokeTarget, events

		var model = await _dbContext.SaveModelAsync(view.DataSource, view.UpdateProcedure(), data, savePrms);

		ISignalResult? signalResult = null;
		if (view.Signal)
		{
			var signal = model.Root.Get<ExpandoObject>("Signal");
			model.Root.Set("Signal", null);
			if (signal != null)
				signalResult = SignalResult.FromData(signal);
		}

		var result = new SaveResult()
		{
			Data = JsonConvert.SerializeObject(model.Root, JsonHelpers.DataSerializerSettings),
			SignalResult = signalResult
		};
		return result;
	}

	public async Task<IInvokeResult> InvokeAsync(String baseUrl, String command, ExpandoObject? data, Action<ExpandoObject> setParams)
	{
		var platformBaseUrl = CreatePlatformUrl(baseUrl);
		var cmd = await _modelReader.GetCommandAsync(platformBaseUrl, command);
		CheckRoles(cmd);

		var prms = cmd.CreateParameters(platformBaseUrl, null, (eo) =>
			{
				setParams?.Invoke(eo);
				eo.Append(data);
			},
			IModelBase.ParametersFlags.SkipId
		);
		setParams?.Invoke(prms);

		var invokeCommand = cmd.GetCommandHandler(_serviceProvider);
		var result = await invokeCommand.ExecuteAsync(cmd, prms);
		//await ProcessDbEvents();
		return result;
	}

	void CheckUserState()
	{
		if (_currentUser.State.IsReadOnly)
			throw new DataServiceException("UI:@[Error.DataReadOnly]");
	}

	void SetReadOnly(IDataModel model)
	{
		if (_currentUser.State.IsReadOnly)
			model.SetReadOnly();
	}

	async Task<IModelView> LoadIndirect(IModelView view, IDataModel? innerModel, Action<ExpandoObject> setParams)
	{
		if (!view.Indirect || innerModel == null)
			return view;

		if (!String.IsNullOrEmpty(view.Target))
		{
			String? targetUrl = innerModel.Root.Resolve(view.Target);
			if (String.IsNullOrEmpty(view.TargetId))
				throw new DataServiceException("targetId must be specified for indirect action");
			targetUrl += "/" + innerModel.Root.Resolve(view.TargetId);

			// TODO: CurrentKind instead UrlKind.Page
			var platformUrl = CreatePlatformUrl(UrlKind.Page, targetUrl);
			view = await LoadViewAsync(platformUrl);

			//var rm = await RequestModel.CreateFromUrl(_codeProvider, rw.CurrentKind, targetUrl);
			//rw = rm.GetCurrentAction();
			if (view.HasModel())
			{
				// TODO: ParameterBuilder
				var indirectParams = view.CreateParameters(platformUrl, setParams);

				var newModel = await _dbContext.LoadModelAsync(view.DataSource, view.LoadProcedure(), indirectParams);
				innerModel.Merge(newModel);
				throw new NotImplementedException("Full URL is required");
				//innerModel.System.Set("__indirectUrl__", view.BaseUrl);
			}
		}
		else
		{
			// simple view/model redirect
			if (view.TargetModel == null)
				throw new DataServiceException("'targetModel' must be specified for indirect action without 'target' property");
			//TODO: view = view.Resolve(innerModel);
			/*
			rw.model = innerModel.Root.Resolve(view.targetModel.model);
			rw.view = innerModel.Root.Resolve(view.targetModel.view);
			rw.viewMobile = innerModel.Root.Resolve(rw.targetModel.viewMobile);
			rw.schema = innerModel.Root.Resolve(rw.targetModel.schema);
			if (String.IsNullOrEmpty(rw.schema))
				rw.schema = null;
			rw.template = innerModel.Root.Resolve(rw.targetModel.template);
			if (String.IsNullOrEmpty(rw.template))
				rw.template = null;
			*/
			if (view.HasModel())
			{
				//loadPrms.Set("Id", platformUrl.Id);
				//var newModel = await _dbContext.LoadModelAsync(view.DataSource, view.LoadProcedure(), loadPrms);
				//innerModel.Merge(newModel);
			}
		}
		return view;
	}

	public async Task<IBlobInfo?> LoadBlobAsync(UrlKind kind, String baseUrl, Action<ExpandoObject> setParams, String? suffix = null)
	{
		var platformUrl = CreatePlatformUrl(kind, baseUrl);
		IModelBlob blob = await _modelReader.GetBlobAsync(platformUrl, suffix)
            ?? throw new DataServiceException($"Blob is null");

        var prms = new ExpandoObject();
		prms.Set("Id", blob.Id);
		prms.Set("Key", blob.Key);
		setParams?.Invoke(prms);

		return blob.Type switch
		{
			ModelBlobType.sql => await LoadBlobSql(blob, prms),
			ModelBlobType.json => await LoadBlobJson(blob, prms),
			ModelBlobType.clr => await LoadBlobClr(blob, prms),
			_ => throw new NotImplementedException(blob.Type.ToString()),
		}; ;
    }

	private Task<BlobInfo?> LoadBlobSql(IModelBlob blob, ExpandoObject prms)
	{
        var loadProc = blob.LoadProcedure();
        if (String.IsNullOrEmpty(loadProc))
            throw new DataServiceException($"LoadProcedure is null");
        return _dbContext.LoadAsync<BlobInfo>(blob?.DataSource, loadProc, prms);
    }
	private async Task<BlobInfo?> LoadBlobClr(IModelBlob modelBlob, ExpandoObject prms)
	{
        if (String.IsNullOrEmpty(modelBlob.ClrType))
            throw new DataServiceException($"ClrType is null");
        var (assembly, clrType) = ClrHelpers.ParseClrType(modelBlob.ClrType);
        var ass = Assembly.Load(assembly);
        var tp = ass.GetType(clrType)
            ?? throw new InvalidOperationException("Type not found");
        var ctor = tp.GetConstructor([typeof(IServiceProvider)])
            ?? throw new InvalidOperationException($"ctor(IServiceProvider) not found in {clrType}");
        var elem = ctor.Invoke(new Object[] { _serviceProvider })
            ?? throw new InvalidOperationException($"Unable to create element of {clrType}");
        if (elem is not IClrInvokeBlob invokeBlob)
            throw new InvalidOperationException($"The type '{clrType}' must implement the interface IClrInvokeBlob");
		var result = await invokeBlob.InvokeAsync(prms);
		return new BlobInfo()
		{
			Name = result.Name,
			Mime = result.Mime,
			Stream = result.Stream
		};
    }

    private async Task<BlobInfo?> LoadBlobJson(IModelBlob modelBlob, ExpandoObject prms)
    {
        var loadProc = modelBlob.LoadProcedure();
        if (String.IsNullOrEmpty(loadProc))
            throw new DataServiceException($"LoadProcedure is null");
        var dm = await _dbContext.LoadModelAsync(modelBlob.DataSource, loadProc, prms);
        var settings = JsonHelpers.IndentedSerializerSettings;
        var json = JsonConvert.SerializeObject(dm.Root, settings);
        Byte[]? stream;
        String mime = MimeTypes.Application.Json;
        if (modelBlob.Zip)
        {
            mime = MimeTypes.Application.Zip;
            stream = ZipUtils.CompressText(json);
        }
        else
        {
			stream = Encoding.UTF8.GetBytes(json);
		}
		return new BlobInfo()
		{
			SkipToken = true,
			Mime = mime,
			Stream = stream,
			Name = modelBlob.OutputFileName
        };
    }

	public async Task<ExpandoObject> SaveFileAsync(String baseUrl, Action<IBlobUpdateInfo> setBlob, Action<ExpandoObject>? setParams)
	{
		var platformUrl = CreatePlatformUrl(UrlKind.File, baseUrl);
		var blobModel = await _modelReader.GetBlobAsync(platformUrl)
            ?? throw new DataServiceException($"Blob is null");
		return blobModel.Type switch
		{
			ModelBlobType.parse => await ParseFile(blobModel, setBlob, setParams),
			_ =>
				throw new NotImplementedException(blobModel.Type.ToString())
		};
    }

	private Task<ExpandoObject> ParseFile(IModelBlob blobModel, Action<IBlobUpdateInfo> setBlob, Action<ExpandoObject>? setParams)
	{
		return blobModel.Parse switch
		{
			ModelParseType.xlsx or ModelParseType.excel => ParseXlsx(blobModel, setBlob, setParams),
			ModelParseType.json => ParseJson(blobModel, setBlob, setParams),
			ModelParseType.auto => ParseAuto(blobModel, setBlob, setParams),
			ModelParseType.csv => ParseCsv(blobModel, setBlob, setParams),
			ModelParseType.dbf => ParseDbf(blobModel, setBlob, setParams),
			ModelParseType.xml => ParseXml(blobModel, setBlob, setParams),
			_ => throw new NotImplementedException(blobModel.Parse.ToString())
		};
	}

	private Task<ExpandoObject> ParseAuto(IModelBlob blobModel, Action<IBlobUpdateInfo> setBlob, Action<ExpandoObject>? setParams)
	{
		BlobUpdateInfo blobInfo = new();
		setBlob(blobInfo);
		if (blobInfo.Name == null)
			throw new InvalidOperationException("Name is null");
		var ext = Path.GetExtension(blobInfo.Name).ToLowerInvariant();
		return ext switch
		{
			".xlsx" => ParseXlsx(blobModel, setBlob, setParams),
			".json" => ParseJson(blobModel, setBlob, setParams),
			".csv" => ParseCsv(blobModel, setBlob, setParams),
			".dbf" => ParseDbf(blobModel, setBlob, setParams),
			".xml" => ParseXml(blobModel, setBlob, setParams),
			_ => throw new NotImplementedException()
		};
	}

	private async Task<ExpandoObject> ParseJson(IModelBlob blobModel, Action<IBlobUpdateInfo> setBlob, Action<ExpandoObject>? setParams)
	{
		BlobUpdateInfo blobInfo = new();
		setBlob(blobInfo);
		if (blobInfo.Stream == null)
			throw new InvalidOperationException("Stream is null");
		String? json;
		if (blobModel.Zip)
			json = ZipUtils.DecompressText(blobInfo.Stream);
		else
		{
			using var sr = new StreamReader(blobInfo.Stream);
			json = sr.ReadToEnd();
		}
		if (json == null)
            throw new InvalidOperationException("Json is null");
        var data = JsonConvert.DeserializeObject<ExpandoObject>(json) ??
			throw new InvalidOperationException("Data is null");
		var prms = new ExpandoObject();
		if (blobModel.Id != null)
			prms.Add("Id", blobModel.Id);
		setParams?.Invoke(prms);
        var res = await _dbContext.SaveModelAsync(blobModel.DataSource, blobModel.UpdateProcedure(), data, prms, null, blobModel.CommandTimeout);
        return res.Root;
    }

	private async Task<ExpandoObject> ParseXlsx(IModelBlob blobModel, Action<IBlobUpdateInfo> setBlob, Action<ExpandoObject>? setParams)
	{
		BlobUpdateInfo blobInfo = new();
		setBlob(blobInfo);
		if (blobInfo.Stream == null)
			throw new InvalidOperationException("Stream is null");
		using var xp = new ExcelParser();
		var dm = xp.CreateDataModel(blobInfo.Stream);

		var prms = new ExpandoObject();
		if (blobModel.Id != null)
			prms.Add("Id", blobModel.Id);
		setParams?.Invoke(prms);
		var res = await _dbContext.SaveModelAsync(blobModel.DataSource, blobModel.UpdateProcedure(), dm.Data, prms, null, blobModel.CommandTimeout);
		return res.Root;
	}
	private Task<ExpandoObject> ParseCsv(IModelBlob blobModel, Action<IBlobUpdateInfo> setBlob, Action<ExpandoObject>? setParams)
	{
		throw new NotImplementedException();
	}

	private Task<ExpandoObject> ParseDbf(IModelBlob blobModel, Action<IBlobUpdateInfo> setBlob, Action<ExpandoObject>? setParams)
	{
		throw new NotImplementedException();
	}

	private Task<ExpandoObject> ParseXml(IModelBlob blobModel, Action<IBlobUpdateInfo> setBlob, Action<ExpandoObject>? setParams)
	{
		throw new NotImplementedException();
	}

	public async Task<IBlobUpdateOutput> SaveBlobAsync(UrlKind kind, String baseUrl, Action<IBlobUpdateInfo> setBlob, String? suffix = null)
	{
		var platformUrl = CreatePlatformUrl(kind, baseUrl);
		var blobModel = await _modelReader.GetBlobAsync(platformUrl, suffix);
		var saveProc = (blobModel?.UpdateProcedure()) 
			?? throw new DataServiceException($"UpdateProcedure is null");
		var blob = new BlobUpdateInfo()
		{
			Key = blobModel?.Key,
			Id = blobModel?.Id
		};
		setBlob(blob);
		var result = await _dbContext.ExecuteAndLoadAsync<BlobUpdateInfo, BlobUpdateOutput>(blobModel?.DataSource, saveProc, blob) 
			?? throw new InvalidOperationException("SaveBlobAsync. Result is null");
		return result;
    }

    public async Task<ILayoutDescription?> GetLayoutDescriptionAsync(String? baseUrl)
	{
		if (baseUrl == null)
			return null;
		var platformUrl = CreatePlatformUrl(UrlKind.Page, baseUrl);
		var view = await _modelReader.TryGetViewAsync(platformUrl);
		if (view == null)
			return null;
		if (view.Styles == null && view.Scripts == null)
			return null;
		return new LayoutDescription(view.Styles, view.Scripts);
	}

	public Byte[] Html2Excel(String html)
	{
		var h = new Html2Excel(_currentUser.Locale.Locale);
		return h.ConvertHtmlToExcel(html);
	}
}

