using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NGS.DatabasePersistence.Oracle.QueryGeneration.Visitors;
using NGS.DomainPatterns;
using Remotion.Linq.Clauses.ResultOperators;

namespace NGS.DatabasePersistence.Oracle.QueryGeneration.QueryComposition
{
	public class MainQueryParts : QueryParts
	{
		public MainQueryParts(
			IServiceLocator locator,
			IOracleConverterFactory factory,
			IEnumerable<IQuerySimplification> simplifications,
			IEnumerable<IExpressionMatcher> expressionMatchers,
			IEnumerable<IMemberMatcher> memberMatchers,
			IEnumerable<IProjectionMatcher> projectionMatchers)
			: base(
				locator,
				string.Empty,
				factory,
				new ParameterAggregator(),
				new QueryContext(false, false, true),
				simplifications,
				expressionMatchers,
				memberMatchers,
				projectionMatchers)
		{
		}

		public string BuildSqlString()
		{
			if (Selects.Count == 0)
				throw new InvalidOperationException("A query must have a select part");

			foreach (var qs in Simplifications)
				if (qs.CanSimplify(this))
					return qs.Simplify(this);

			var countOperator = ResultOperators.FirstOrDefault(it => it is CountResultOperator || it is LongCountResultOperator);
			if (countOperator != null)
				return BuildCountQuery(countOperator);

			var sb = new StringBuilder();
			sb.Append("SELECT ");
			sb.Append(string.Join(@", 
	", Selects.Where(it => it.Sql != null).Select(it => it.Sql))).AppendLine();
			sb.Append(GetFromPart());
			sb.Append(GetWherePart());
			sb.Append(GetOrderPart());

			ProcessResultOperators(sb);

			return sb.ToString();
		}
	}
}