﻿// Copyright © 2021 Alex Kukhtin. All rights reserved.

using System;

namespace A2v10.Identity.Jwt
{
#pragma warning disable IDE1006 // Naming Styles
    public record JwtTokenResponse(String accessToken, String refreshToken, Int64 validTo, String user, Boolean success = true );
#pragma warning restore IDE1006 // Naming Styles


	public record JwtTokenResult(DateTime Expires, JwtTokenResponse Response);
}