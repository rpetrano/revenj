﻿using System.ComponentModel.Composition;
using System.ServiceProcess;
using System.Threading;
using DSL;
using NGS.DomainPatterns;
using NGS.Extensibility;

namespace Revenj.Http
{
	[Export(typeof(ServiceBase))]
	[ExportMetadata(Metadata.ClassType, typeof(HttpService))]
	partial class HttpService : ServiceBase
	{
		private Thread HttpThread;
		private readonly IServiceLocator Locator;

		public HttpService()
		{
			InitializeComponent();
			Locator = Platform.Start();
		}

		protected override void OnStart(string[] args)
		{
			HttpThread = new Thread(RunListener);
			HttpThread.Start();
		}

		private void RunListener()
		{
			var server = Locator.Resolve<HttpServer>();
			server.Run();
		}

		protected override void OnStop()
		{
			HttpThread.Abort();
		}
	}
}
