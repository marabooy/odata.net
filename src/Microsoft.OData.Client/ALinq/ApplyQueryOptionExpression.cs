﻿//---------------------------------------------------------------------
// <copyright file="ApplyQueryOptionExpression.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData.Client
{
    using System;
	using System.Collections.Generic;
	using System.Linq.Expressions;
	using Microsoft.OData.UriParser.Aggregation;

	/// <summary>
	/// A resource specific expression representing an apply query option.
	/// </summary>
	internal class ApplyQueryOptionExpression : QueryOptionExpression
	{
		/// <summary>
		/// The aggregations for apply query option.
		/// </summary>
		private List<Aggregation> aggregations;

		/// <summary>
		/// The individual expressions that make up the GroupBy selector.
		/// </summary>
		private List<Expression> groupingExpressions;

		/// <summary>
		/// Creates an <see cref="ApplyQueryOptionExpression"/> expression.
		/// </summary>
		/// <param name="type">the return type of the expression.</param>
		internal ApplyQueryOptionExpression(Type type)
			: base(type)
		{
			this.aggregations = new List<Aggregation>();
			this.groupingExpressions = new List<Expression>();
		}

		/// <summary>
		/// The <see cref="ExpressionType"/> of the <see cref="Expression"/>.
		public override ExpressionType NodeType
		{
			get { return (ExpressionType)ResourceExpressionType.ApplyQueryOption; }
		}

		/// <summary>
		/// Aggregations in the apply expression
		/// </summary>
		internal List<Aggregation> Aggregations
		{
			get
			{
				return this.aggregations;
			}
		}

		/// <summary>
		/// The individual expressions that make up the GroupBy selector.
		/// </summary>
		internal List<Expression> GroupingExpressions
        {
            get
            {
				return this.groupingExpressions;
            }
        }

		/// <summary>
		/// Structure for an aggregation. Holds lambda expression plus enum indicating aggregation method
		/// </summary>
		internal struct Aggregation
		{
			/// <summary>
			/// lambda expression for aggregation selector
			/// </summary>
			internal readonly Expression Expression;

			/// <summary>
			/// enum indicating aggregation method
			/// </summary>
			internal readonly AggregationMethod AggregationMethod;

			/// <summary>
			/// Creates an aggregation
			/// </summary>
			/// <param name="exp">lambda expression for aggregation selector</param>
			/// <param name="aggregationMethod"> enum indicating aggregation method
			internal Aggregation(Expression exp, AggregationMethod aggregationMethod)
			{
				this.Expression = exp;
				this.AggregationMethod = aggregationMethod;
			}
		}
	}
}
