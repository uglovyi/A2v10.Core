﻿// Copyright © 2015-2023 Oleksandr Kukhtin. All rights reserved.


using A2v10.Infrastructure;

namespace A2v10.Xaml;
public abstract class UIElementBase : XamlElement, IXamlElement
{
	public Boolean? If { get; set; }
	public Boolean? Show { get; set; }
	public Boolean? Hide { get; set; }
	public RenderMode? Render { get; set; }

	public Boolean IsInGrid { get; set; }

	public Thickness? Margin { get; set; }
	public Thickness? Padding { get; set; }
	public WrapMode Wrap { get; set; }
	public Thickness? Absolute { get; set; }
	public String? HtmlId { get; set; }

	public Object? XamlStyle { get; set; }

	public String? Tip { get; set; }

	public abstract void RenderElement(RenderContext context, Action<TagBuilder>? onRender = null);

	[Flags]
	public enum MergeAttrMode
	{
		Visibility = 0x01,
		Margin = 0x02,
		Wrap = 0x04,
		Tip = 0x08,
		Content = 0x10,
		TabIndex = 0x20,
		All = Visibility | Margin | Wrap | Tip | Content | TabIndex,
		NoContent = Visibility | Margin | Wrap | Tip | TabIndex,
		NoTabIndex = Visibility | Margin | Wrap | Tip | Content,
		SpecialTab = Visibility | Margin | Wrap | Tip,
		SpecialWizardPage = Margin | Wrap | Tip
	}

	protected virtual void MergeVisibilityAttribures(TagBuilder tag, RenderContext context)
	{
		MergeBindingAttributeBool(tag, context, "v-if", nameof(If), If);
		MergeBindingAttributeBool(tag, context, "v-show", nameof(Show), Show);
		// emulate v-hide
		MergeBindingAttributeBool(tag, context, "v-show", nameof(Hide), Hide, bInvert: true);
	}

	public virtual void MergeAttributes(TagBuilder tag, RenderContext context, MergeAttrMode mode = MergeAttrMode.All)
	{
		if (mode.HasFlag(MergeAttrMode.Visibility))
		{
			MergeVisibilityAttribures(tag, context);
		}

		if (mode.HasFlag(MergeAttrMode.Tip))
		{
			MergeBindingAttributeString(tag, context, "title", "Tip", Tip);
		}

		if (mode.HasFlag(MergeAttrMode.Wrap))
		{
			if (Wrap != WrapMode.Default)
				tag.AddCssClass(Wrap.ToString().ToKebabCase());
		}

		if (mode.HasFlag(MergeAttrMode.Margin))
		{
			Margin?.MergeStyles("margin", tag);
			Padding?.MergeStyles("padding", tag);
		}

		Absolute?.MergeAbsolute(tag);

		tag.MergeAttribute("id", HtmlId);
	}

	protected static void RenderContent(RenderContext context, Object? content)
	{
		// if it's a binding, it will be added via MergeAttribute
		if (content == null)
			return;
		if (content is UIElementBase uiElem)
			uiElem.RenderElement(context);
		else if (content != null)
			context.Writer.Write(context.LocalizeCheckApostrophe(content.ToString()!.Replace("\\n", "<br>")));
	}

	protected void MergeBindingAttributeString(TagBuilder tag, RenderContext context, String attrName, String propName, String? propValue)
	{
		var attrBind = GetBinding(propName);
		if (attrBind != null)
			tag.MergeAttribute($":{attrName}", attrBind.GetPathFormat(context));
		else if (propValue != null)
			tag.MergeAttribute(attrName, context.Localize(propValue));
	}

	protected void MergeAttributeInt32(TagBuilder tag, RenderContext context, String attrName, String propName, Int32? propValue)
	{
		var attrBind = GetBinding(propName);
		if (attrBind != null)
			tag.MergeAttribute($":{attrName}", attrBind.GetPath(context));
		else if (propValue != null)
			tag.MergeAttribute($":{attrName}", propValue.ToString()!);
	}

	protected void AddBindingCssClass(TagBuilder tag, RenderContext context, String? propValue)
	{
		var cssBind = GetBinding("CssClass");
		if (cssBind != null)
			tag.MergeAttribute(":class", cssBind.GetPath(context));
		else
			tag.AddCssClass(propValue);
	}

	protected void MergeValueItemProp(TagBuilder input, RenderContext context, String valueName)
	{
		var valBind = GetBinding(valueName);
		if (valBind == null)
			return;
		// split to path and property
		String path = valBind.GetPath(context);
		(String Path, String Prop) = SplitToPathProp(path);
		if (String.IsNullOrEmpty(Path) || String.IsNullOrEmpty(Prop))
			throw new XamlException($"Invalid binding for {valueName} '{path}'");
		input.MergeAttribute(":item", Path);
		input.MergeAttribute("prop", Prop);
		if (valBind.DataType != DataType.String)
			input.MergeAttribute("data-type", valBind.DataType.ToString());
		if (!String.IsNullOrEmpty(valBind.Format))
			input.MergeAttribute("format", valBind.Format);
		else
		{
			var valBindFormat = valBind.GetBinding("Format");
			if (valBindFormat != null)
				input.MergeAttribute(":format", valBindFormat.GetPath(context));
		}
		var maskBind = valBind.GetBinding("Mask");
		if (maskBind != null)
			input.MergeAttribute(":mask", maskBind.GetPathFormat(context));
		else if (!String.IsNullOrEmpty(valBind.Mask))
			input.MergeAttribute("mask", valBind.Mask);
		if (valBind.HideZeros)
			input.MergeAttribute(":hide-zeros", "true");
		if (valBind.HasFilters)
			input.MergeAttribute(":filters", valBind.FiltersJS());
	}


	protected void MergeValidateValueItemProp(TagBuilder input, RenderContext context, String valueName)
	{
		var valBind = GetBinding(valueName);
		if (valBind == null)
			return;
		// split to path and property
		String path = valBind.GetPath(context);
		var (Path, Prop) = SplitToPathProp(path);
		if (String.IsNullOrEmpty(Path) || String.IsNullOrEmpty(Prop))
			throw new XamlException($"Invalid binding for {valueName} '{path}'");
		input.MergeAttribute(":item-to-validate", Path);
		input.MergeAttribute("prop-to-validate", Prop);
	}

	protected void MergeCustomValueItemProp(TagBuilder input, RenderContext context, String valueName, String prefix)
	{
		var valBind = GetBinding(valueName);
		if (valBind == null)
			return;
		// split to path and property
		String path = valBind.GetPath(context);
		(String Path, String Prop) = SplitToPathProp(path);
		if (String.IsNullOrEmpty(Path) || String.IsNullOrEmpty(Prop))
			throw new XamlException($"Invalid binding for {valueName} '{path}'");
		input.MergeAttribute($":{prefix}-item", Path);
		input.MergeAttribute($"{prefix}-prop", Prop);
	}

	protected static (String Path, String Prop) SplitToPathProp(String path)
	{
		var result = (Path: "", Prop: "");

		if (String.IsNullOrEmpty(path))
			return result;

		Int32 ix = path.LastIndexOf('.');
		if (ix != -1)
		{
			result.Prop = path[(ix + 1)..];
			result.Path = path[..ix];
		}
		else
		{
			result.Prop = path;
			result.Path = "$data";
		}
		return result;
	}

	protected void RenderBadge(RenderContext context, String? badge, String? cssClass = "badge")
	{
		var badgeBind = GetBinding("Badge");
		if (badgeBind != null)
		{
			new TagBuilder("span", cssClass)
				.MergeAttribute("v-text", badgeBind.GetPathFormat(context))
				.MergeAttribute("v-if", badgeBind.GetPathFormat(context))
				.Render(context);
		}
		else if (!String.IsNullOrEmpty(badge))
		{
			new TagBuilder("span", cssClass)
				.SetInnerText(context.LocalizeCheckApostrophe(badge))
				.Render(context);
		}
	}

	protected virtual void MergeAlign(TagBuilder input, RenderContext context, TextAlign align)
	{
		var alignProp = GetBinding("Align");
		if (alignProp != null)
			input.MergeAttribute(":align", alignProp.GetPath(context));
		else if (align != TextAlign.Default)
			input.MergeAttribute("align", align.ToString().ToLowerInvariant());
	}

	protected virtual Boolean SkipRender(RenderContext context)
	{
		var rm = GetRenderMode(context);
		if (rm == null)
			return false;
		if (rm == RenderMode.Hide)
			return true;
		if (rm == RenderMode.Debug)
			return !context.IsDebugConfiguration;
		return false;
	}

	protected RenderMode? GetRenderMode(RenderContext context)
	{
		var renderBind = GetBinding(nameof(Render));
		if (renderBind == null && Render == null)
			return null;
		if (renderBind != null)
		{
			var rm = context.CalcDataModelExpression(renderBind.Path);
			if (rm is String rmString)
			{
				if (Enum.TryParse<RenderMode>(rmString, out RenderMode rmResult))
				{
					return rmResult;
				}
				throw new XamlException($"Invalid RenderMode '{rmResult}', Expected 'Show', 'Hide', 'ReadOnly' or 'Debug'");
			}
			else if (rm is Boolean rmBool)
				return rmBool ? RenderMode.Show : RenderMode.Hide;
		}
		else if (Render != null)
		{
			return Render;
		}
		return null;
	}

	protected override void OnEndInit()
	{
		base.OnEndInit();
	}

	public override void OnSetStyles(RootContainer root)
	{
		switch (XamlStyle)
		{
			case StyleDescriptor sd:
				sd.Set(this, root);
				break;
			case Style st:
				st.Set(this);
				break;
			case null:
				if (root.Styles != null && root.Styles.TryGetValue(this.GetType().Name, out Style? defaultStyle))
					defaultStyle?.Set(this);
				break;
		}
	}
}

