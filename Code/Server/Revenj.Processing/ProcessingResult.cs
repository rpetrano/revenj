﻿using System.Collections.Generic;
using System.Net;

namespace Revenj.Processing
{
	public class ProcessingResult<TFormat> : IProcessingResult<TFormat>
	{
		public string Message { get; private set; }
		public HttpStatusCode Status { get; private set; }
		public IEnumerable<ICommandResultDescription<TFormat>> ExecutedCommandResults { get; private set; }
		public long Duration { get; private set; }

		public static ProcessingResult<TFormat> Create(
			string message,
			HttpStatusCode status,
			IEnumerable<ICommandResultDescription<TFormat>> executedCommands,
			long duration)
		{
			return new ProcessingResult<TFormat>
			{
				Message = message,
				Status = status,
				ExecutedCommandResults = executedCommands,
				Duration = duration
			};
		}
	}
}
