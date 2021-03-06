﻿using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using NGS.DatabasePersistence.Oracle.QueryGeneration.QueryComposition;
using NGS.DatabasePersistence.Oracle.QueryGeneration.Visitors;
using NGS.DomainPatterns;
using Remotion.Linq.Clauses.Expressions;

namespace NGS.Plugins.DatabasePersistence.Oracle.ExpressionSupport
{
	[Export(typeof(IExpressionMatcher))]
	public class LinqMethods : IExpressionMatcher
	{
		private delegate bool MethodCallDelegate(MethodCallExpression methodCall, StringBuilder queryBuilder, Action<Expression> visitExpression);

		private static Dictionary<MethodInfo, MethodCallDelegate> SupportedMethods;
		static LinqMethods()
		{
			SupportedMethods = new Dictionary<MethodInfo, MethodCallDelegate>();
			SupportedMethods.Add(typeof(Enumerable).GetMethod("ToArray"), ArrayAgg);
			SupportedMethods.Add(typeof(Enumerable).GetMethod("ToList"), ArrayAgg);
		}

		public bool TryMatch(Expression expression, StringBuilder queryBuilder, Action<Expression> visitExpression, QueryContext context)
		{
			var mce = expression as MethodCallExpression;
			if (mce == null || !mce.Method.IsGenericMethod)
				return false;

			MethodCallDelegate mcd;
			if (SupportedMethods.TryGetValue(mce.Method.GetGenericMethodDefinition(), out mcd))
			{
				return mcd(mce, queryBuilder, visitExpression);
			}
			return false;
		}

		private static string GetTypeName(Type array)
		{
			var element = array.IsArray
				? array.GetElementType()
				: array.IsGenericType && (array.GetGenericTypeDefinition() == typeof(List<>)
										|| array.GetGenericTypeDefinition() == typeof(HashSet<>))
					? array.GetGenericArguments()[0]
					: null;
			if (element == null)
				return null;
			if (element == typeof(string))
				return "\"-NGS-\".CLOB_ARR";
			if (element == typeof(bool) || element == typeof(bool?))
				return "\"-NGS-\".BOOL_ARR";
			if (element == typeof(int) || element == typeof(int?))
				return "\"-NGS-\".INT_ARR";
			if (element == typeof(decimal) || element == typeof(decimal?))
				return "\"-NGS-\".NUMBER_ARR";
			if (element == typeof(long) || element == typeof(long?))
				return "\"-NGS-\".LONG_ARR";
			if (element == typeof(float) || element == typeof(float?))
				return "\"-NGS-\".FLOAT_ARR";
			if (element == typeof(double) || element == typeof(double?))
				return "\"-NGS-\".DOUBLE_ARR";
			if (element == typeof(DateTime) || element == typeof(DateTime?))
				return "\"-NGS-\".TWTZ_ARR";
			if (element == typeof(Guid) || element == typeof(Guid?))
				return "\"-NGS-\".GUID_ARR";
			if (typeof(IAggregateRoot).IsAssignableFrom(element))
				return "\"" + element.Namespace + "\".\"-" + element.Name + "-EA-\"";
			if (typeof(IAggregateRoot).IsAssignableFrom(element))
				return "\"" + element.Namespace + "\".\"-" + element.Name + "-SA-\"";
			//TODO missing value types
			return null;
		}

		private static bool ArrayAgg(MethodCallExpression methodCall, StringBuilder queryBuilder, Action<Expression> visitExpression)
		{
			var typeName = GetTypeName(methodCall.Type);
			if (typeName == null)
				return false;
			var sqe = methodCall.Arguments[0] as SubQueryExpression;
			if (sqe != null)
			{
				var me = sqe.QueryModel.SelectClause.Selector as MemberExpression;
				queryBuilder.Append("(SELECT cast(collect(");
				if (me != null)
					queryBuilder.Append('"').Append(me.Member.Name).Append('"');
				else //TODO detect supported selectors - only identity
					visitExpression(sqe.QueryModel.SelectClause.Selector);
				queryBuilder.Append(')');
			}
			else queryBuilder.Append("SELECT cast(collect($sq) ");
			queryBuilder.Append(" AS ").Append(typeName).Append(") FROM ");
			if (methodCall.Arguments[0] is MemberExpression)
				queryBuilder.Append("TABLE(");
			else queryBuilder.Append('(');
			visitExpression(methodCall.Arguments[0]);
			queryBuilder.Append(") $sq)");

			return true;
		}
	}
}
