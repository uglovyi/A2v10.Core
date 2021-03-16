﻿// Copyright © 2021 Alex Kukhtin. All rights reserved.

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Web;

using A2v10.Infrastructure;

namespace A2v10.Services
{
	public class PlatformUrl : IPlatformUrl
	{
		public PlatformUrl(UrlKind kind, String url, String id = null)
		{
			var nurl = NormalizePath(url);
			Kind = kind;
			var parts = ("_/" + nurl.Path).Split(new Char[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar });
			Construct(parts, nurl.Query, id);
		}

		public PlatformUrl(String url, String id = null)
		{
			var nurl = NormalizePath(url);

			var parts = nurl.Path.Split('/');

			Kind = parts[0] switch
			{
				"_page" => UrlKind.Page,
				"_dialog" => UrlKind.Dialog,
				"_popup" => UrlKind.Popup,
				"_image" => UrlKind.Image,
				_ => UrlKind.Undefined
			};
			Construct(parts, nurl.Query, id);
		}

		public String LocalPath { get; set; }
		public String BaseUrl { get; private set; }

		public UrlKind Kind { get; private set; }
		public String Action { get; private set; }
		public String Id { get; private set; }

		public ExpandoObject Query { get; private set; }


		public void Redirect(String path)
		{
			if (LocalPath == path)
				return;
			LocalPath = path;
		}

		static (String Path, String Query) NormalizePath(String path)
		{
			String query = null;
			if (path.Contains('?'))
			{
				var px = path.Split('?');
				path = px[0];
				query = px[1];
			}
				
			path = path.ToLowerInvariant().Replace('\\', '/');
			if (path.StartsWith('/'))
				path = path[1..];
			return (path, query);
		}

		void Construct(String[] parts, String query, String id)
		{
			Int32 len = parts.Length;
			Id = parts[len - 1];
			if (String.IsNullOrEmpty(id))
			{
				/* HACK? */
				if (String.IsNullOrEmpty(Id) || Id == "0" || Id.Equals("new", StringComparison.OrdinalIgnoreCase))
					Id = null;
			}
			else
				Id = id;
			Action = parts[len - 2];
			var pathArr = new ArraySegment<String>(parts, 1, len - 3);
			LocalPath = String.Join("/", pathArr);
			// baseUrl with action and id
			var baseArr = new List<String>(pathArr)
			{
				Action
			};
			if (Id != null)
				baseArr.Add(Id);
			baseArr.Add(String.Empty); // for last slash

			BaseUrl = String.Join("/", baseArr);

			if (!String.IsNullOrEmpty(query))
			{
				var eo = new ExpandoObject();
				var nvc = HttpUtility.ParseQueryString(query);
				foreach (var k in nvc.AllKeys)
					AddQueryParam(eo, k, nvc[k]);
				if (!eo.IsEmpty())
					Query = eo;
			}
		}

		static void AddQueryParam(ExpandoObject eo, String key, String value)
		{
			if (!key.Equals("period", StringComparison.OrdinalIgnoreCase))
			{
				eo.Set(key.ToPascalCase(), value);
			}
			else
			{
				var ps = value.Split('-');
				eo.RemoveKeys("From"); // replace prev value
				eo.RemoveKeys("To");
				if (ps[0].ToLowerInvariant() == "all")
				{
					// from js! utils.date.minDate/maxDate
					eo.Set("From", "19010101");
					eo.Set("To", "29991231");
				}
				else
				{
					eo.Set("From", ps[0]);
					eo.Set("To", ps.Length == 2 ? ps[1] : ps[0]);
				}
			}
		}
	}
}
