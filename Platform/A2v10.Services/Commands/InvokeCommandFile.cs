﻿// Copyright © 2020-2023 Oleksandr Kukhtin. All rights reserved.

using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;


namespace A2v10.Services;

public class InvokeCommandFile(IServiceProvider services) : IModelInvokeCommand
{
	private readonly IServiceProvider _serivceProvider = services;
	private readonly ICurrentUser _currentUser = services.GetRequiredService<ICurrentUser>();

    #region IModelInvokeCommand
    public Task<IInvokeResult> ExecuteAsync(IModelCommand command, ExpandoObject parameters)
	{
		throw new NotImplementedException();
	}
	#endregion
}
