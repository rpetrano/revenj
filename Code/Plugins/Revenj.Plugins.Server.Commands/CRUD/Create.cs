﻿using System;
using System.ComponentModel.Composition;
using System.Diagnostics.Contracts;
using System.Net;
using System.Runtime.Serialization;
using NGS;
using NGS.Common;
using NGS.DomainPatterns;
using NGS.Extensibility;
using NGS.Security;
using NGS.Serialization;
using NGS.Utility;
using Revenj.Processing;

namespace Revenj.Plugins.Server.Commands
{
	[Export(typeof(IServerCommand))]
	[ExportMetadata(Metadata.ClassType, typeof(Create))]
	public class Create : IServerCommand
	{
		private readonly IServiceLocator Locator;
		private readonly IDomainModel DomainModel;
		private readonly IPermissionManager Permissions;

		public Create(
			IServiceLocator locator,
			IDomainModel domainModel,
			IPermissionManager permissions)
		{
			Contract.Requires(locator != null);
			Contract.Requires(domainModel != null);
			Contract.Requires(permissions != null);

			this.Locator = locator;
			this.DomainModel = domainModel;
			this.Permissions = permissions;
		}

		[DataContract(Namespace = "")]
		public class Argument<TFormat>
		{
			[DataMember]
			public string Name;
			[DataMember]
			public TFormat Data;
		}

		private static TFormat CreateExampleArgument<TFormat>(ISerialization<TFormat> serializer)
		{
			return serializer.Serialize(new Argument<TFormat> { Name = "Module.AggregateRoot" });
		}

		private static TFormat CreateExampleArgument<TFormat>(ISerialization<TFormat> serializer, Type rootType)
		{
			try
			{
				return
					serializer.Serialize(
						new Argument<TFormat>
						{
							Name = rootType.FullName,
							Data = serializer.Serialize((dynamic)TemporaryResources.CreateRandomObject(rootType))
						});
			}
			catch
			{
				//fallback to simple example since sometimes calculated properties will throw exception during serialization
				return CreateExampleArgument(serializer);
			}
		}

		public ICommandResult<TOutput> Execute<TInput, TOutput>(ISerialization<TInput> input, ISerialization<TOutput> output, TInput data)
		{
			var either = CommandResult<TOutput>.Check<Argument<TInput>, TInput>(input, output, data, CreateExampleArgument);
			if (either.Error != null)
				return either.Error;
			var argument = either.Argument;

			var rootType = DomainModel.Find(argument.Name);
			if (rootType == null)
				return CommandResult<TOutput>.Fail(
					"Couldn't find aggregate root type {0}.".With(argument.Name),
					@"Example argument: 
" + CommandResult<TOutput>.ConvertToString(CreateExampleArgument(output)));

			if (!typeof(IAggregateRoot).IsAssignableFrom(rootType))
				return CommandResult<TOutput>.Fail(@"Specified type ({0}) is not an aggregate root. 
Please check your arguments.".With(argument.Name), null);

			if (!Permissions.CanAccess(rootType))
				return
					CommandResult<TOutput>.Return(
						HttpStatusCode.Forbidden,
						default(TOutput),
						"You don't have permission to access: {0}.",
						argument.Name);

			if (argument.Data == null)
				return CommandResult<TOutput>.Fail("Data to create not specified.", @"Example argument: 
" + CommandResult<TOutput>.ConvertToString(CreateExampleArgument(output, rootType)));

			try
			{
				var commandType = typeof(CreateCommand<>).MakeGenericType(rootType);
				var command = Activator.CreateInstance(commandType) as ICreateCommand;
				var result = command.Create(input, output, Locator, argument.Data);

				return CommandResult<TOutput>.Return(HttpStatusCode.Created, result, "Object created");
			}
			catch (ArgumentException ex)
			{
				return CommandResult<TOutput>.Fail(
					ex.Message,
					ex.GetDetailedExplanation() + @"
Example argument: 
" + CommandResult<TOutput>.ConvertToString(CreateExampleArgument(output, rootType)));
			}
		}

		private interface ICreateCommand
		{
			TOutput Create<TInput, TOutput>(
				ISerialization<TInput> input,
				ISerialization<TOutput> output,
				IServiceLocator locator,
				TInput data);
		}

		private class CreateCommand<TRoot> : ICreateCommand
			where TRoot : IAggregateRoot
		{
			public TOutput Create<TInput, TOutput>(
				ISerialization<TInput> input,
				ISerialization<TOutput> output,
				IServiceLocator locator,
				TInput data)
			{
				TRoot root;
				try
				{
					root = input.Deserialize<TInput, TRoot>(data, locator);
				}
				catch (Exception ex)
				{
					throw new ArgumentException(
						"Error deserializing: " + ex.Message,
						new FrameworkException(@"Sent data:
{0}".With(data), ex));
				}
				try
				{
					var repository = locator.Resolve<IPersistableRepository<TRoot>>();
					repository.Insert(root);
					return output.Serialize(root);
				}
				catch (Exception ex)
				{
					string additionalInfo;
					try
					{
						additionalInfo = @"Sent data:
" + input.Serialize(root);
					}
					catch (Exception sex)
					{
						additionalInfo = "Error serializing input: " + sex.Message;
					}
					throw new ArgumentException(
						"Error saving: " + ex.Message,
						new FrameworkException(additionalInfo, ex));
				}
			}
		}
	}
}
