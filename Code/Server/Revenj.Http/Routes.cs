using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Net;
using System.ServiceModel.Web;
using System.Xml.Linq;
using NGS.DomainPatterns;

namespace Revenj.Http
{
	internal class Routes
	{
		private readonly Dictionary<string, List<RouteHandler>> MethodRoutes = new Dictionary<string, List<RouteHandler>>();
		private readonly ConcurrentDictionary<string, KeyValuePair<RouteHandler, Uri>> Cache = new ConcurrentDictionary<string, KeyValuePair<RouteHandler, Uri>>();

		public Routes(IServiceLocator locator)
		{
			var config = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
			var xml = XElement.Load(config.FilePath, LoadOptions.None);
			var sm = xml.Element("system.serviceModel");
			if (sm == null)
				throw new ConfigurationErrorsException("Services not defined. system.serviceModel missing from configuration");
			var she = sm.Element("serviceHostingEnvironment");
			if (she == null)
				throw new ConfigurationErrorsException("Services not defined. serviceHostingEnvironment missing from configuration");
			var sa = she.Element("serviceActivations");
			if (sa == null)
				throw new ConfigurationErrorsException("Services not defined. serviceActivations missing from configuration");
			var services = sa.Elements("add").ToList();
			if (services.Count == 0)
				throw new ConfigurationErrorsException("Services not defined. serviceActivations elements not defined in configuration");
			foreach (XElement s in services)
				ConfigureService(s, locator);
		}

		private void ConfigureService(XElement service, IServiceLocator locator)
		{
			var attributes = service.Attributes().ToList();
			var ra = attributes.FirstOrDefault(it => "relativeAddress".Equals(it.Name.LocalName, StringComparison.InvariantCultureIgnoreCase));
			var serv = attributes.FirstOrDefault(it => "service".Equals(it.Name.LocalName, StringComparison.InvariantCultureIgnoreCase));
			if (serv == null || string.IsNullOrEmpty(serv.Value))
				throw new ConfigurationErrorsException("Missing service type on serviceActivation element: " + service.ToString());
			if (ra == null || string.IsNullOrEmpty(ra.Value))
				throw new ConfigurationErrorsException("Missing relative address on serviceActivation element: " + service.ToString());
			var type = Type.GetType(serv.Value);
			if (type == null)
				throw new ConfigurationErrorsException("Invalid service defined in " + ra.Value + ". Type " + serv.Value + " not found.");
			var instance = locator.Resolve(type, null);
			foreach (var i in type.GetInterfaces())
			{
				foreach (var m in i.GetMethods())
				{
					var inv = (WebInvokeAttribute[])m.GetCustomAttributes(typeof(WebInvokeAttribute), false);
					var get = (WebGetAttribute[])m.GetCustomAttributes(typeof(WebGetAttribute), false);
					foreach (var at in inv)
					{
						var rh = new RouteHandler(ra.Value, at.UriTemplate, instance, m);
						Add(at.Method, rh);
					}
					foreach (var at in get)
					{
						var rh = new RouteHandler(ra.Value, at.UriTemplate, instance, m);
						Add("GET", rh);
					}
				}
			}
		}

		private void Add(string method, RouteHandler handler)
		{
			List<RouteHandler> list;
			if (!MethodRoutes.TryGetValue(method, out list))
				MethodRoutes[method] = list = new List<RouteHandler>();
			list.Add(handler);
		}

		public RouteHandler Find(string httpMethod, Uri url, out UriTemplateMatch templateMatch)
		{
			var reqId = httpMethod + ":" + url.LocalPath;
			KeyValuePair<RouteHandler, Uri> rh;
			if (Cache.TryGetValue(reqId, out rh))
			{
				templateMatch = rh.Key.Template.Match(rh.Value, url);
				return rh.Key;
			}
			templateMatch = null;
			List<RouteHandler> handlers;
			if (!MethodRoutes.TryGetValue(httpMethod, out handlers))
				return null;
			var urlstr = url.ToString();
			var baseAddr = new Uri(urlstr.Substring(0, urlstr.Length - url.PathAndQuery.Length));
			foreach (var h in handlers)
			{
				var match = h.Template.Match(baseAddr, url);
				if (match != null)
				{
					templateMatch = match;
					Cache.TryAdd(reqId, new KeyValuePair<RouteHandler, Uri>(h, baseAddr));
					return h;
				}
			}
			return null;
		}

		public RouteHandler Find(HttpListenerRequest request, out UriTemplateMatch templateMatch)
		{
			return Find (request.HttpMethod, request.Url, out templateMatch);
		}
	}
}
