//---------------------------------------------------------------------
// <copyright file="DollarApplyTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.OData.Edm;
using Xunit;

namespace Microsoft.OData.Client.Tests.ALinq
{
    public class Number
    {
        public int Id { get; set; }
        public int IntProp { get; set; }
        public int? NullableIntProp { get; set; }
        public double DoubleProp { get; set; }
        public double? NullableDoubleProp { get; set; }
        public decimal DecimalProp { get; set; }
        public decimal? NullableDecimalProp { get; set; }
        public long LongProp { get; set; }
        public long? NullableLongProp { get; set; }
        public float SingleProp { get; set; }
        public float? NullableSingleProp { get; set; }
    }

    public class DollarApplyTests
    {
        private readonly DataServiceContext context;
        private const string serviceUri = "http://tempuri.org";
        private const string numbersEntitySetName = "Numbers";
        private static string aggregateUrlTemplate = serviceUri + '/' + numbersEntitySetName + "?$apply=aggregate({0} with {1} as {2})";

        public static IEnumerable<object[]> GetApplyQueryOptionData()
        {
            var testData = new List<object[]>();

            foreach(var aggregationMethod in new[] { "Sum", "Average", "Min", "Max" })
            {
                foreach(var type in new[] { "Int", "Double", "Decimal", "Long", "Single"})
                {
                    var aggregationMethodToLower = aggregationMethod.ToLower(); // e.g. sum
                    var propertyName = type + "Prop";  // e.g. IntProp
                    var aggregationAlias = aggregationMethod + propertyName; // e.g. SumIntProp
                    var nullablePropertyName = "Nullable" + propertyName; // e.g. NullableIntProp
                    var nullableAggregationAlias = aggregationMethod + nullablePropertyName; // e.g. SumNullableIntProp

                    testData.Add(new object[] { aggregationMethod, propertyName, string.Format(aggregateUrlTemplate, propertyName, aggregationMethodToLower, aggregationAlias) });
                    testData.Add(new object[] { aggregationMethod, nullablePropertyName, string.Format(aggregateUrlTemplate, nullablePropertyName, aggregationMethodToLower, nullableAggregationAlias) });
                    // e.g.
                    // { "Sum", "IntProp", "http://tempuri.org/Numbers?$apply=aggregate(IntProp with sum as SumIntProp)" }
                    // { "Sum", "NullableIntProp", "http://tempuri.org/Numbers?$apply=aggregate(NullableIntProp with sum as SumNullableIntProp)" }
                }
            }

            foreach (var item in testData)
                yield return item;
        }

        public DollarApplyTests()
        {
            var model = new EdmModel();
            var entity = new EdmEntityType("NS", "Number");
            entity.AddKeys(entity.AddStructuralProperty("Id", EdmCoreModel.Instance.GetInt32(false)));
            entity.AddStructuralProperty("IntProp", EdmCoreModel.Instance.GetInt32(false));
            entity.AddStructuralProperty("NullableIntProp", EdmCoreModel.Instance.GetInt32(true));
            entity.AddStructuralProperty("DoubleProp", EdmCoreModel.Instance.GetDouble(false));
            entity.AddStructuralProperty("NullableDoubleProp", EdmCoreModel.Instance.GetDouble(true));
            entity.AddStructuralProperty("DecimalProp", EdmCoreModel.Instance.GetDecimal(false));
            entity.AddStructuralProperty("NullableDecimalProp", EdmCoreModel.Instance.GetDecimal(true));
            entity.AddStructuralProperty("LongProp", EdmCoreModel.Instance.GetInt64(false));
            entity.AddStructuralProperty("NullableLongProp", EdmCoreModel.Instance.GetInt64(true));
            entity.AddStructuralProperty("SingleProp", EdmCoreModel.Instance.GetSingle(false));
            entity.AddStructuralProperty("NullableSingleProp", EdmCoreModel.Instance.GetSingle(true));

            var container = new EdmEntityContainer("NS", "Container");

            model.AddElement(entity);
            model.AddElement(container);

            container.AddEntitySet(numbersEntitySetName, entity);

            context = new DataServiceContext(new Uri(serviceUri));
            context.Format.UseJson(model);
        }

        [Theory]
        [MemberData(nameof(GetApplyQueryOptionData))]
        public void TranslateLinqAggregateExpressionToExpectedAggregateUri(string aggregationMethod, string propertyName, string expectedAggregateUri)
        {
            DataServiceQuery<Number> queryable = this.context.CreateQuery<Number>(numbersEntitySetName);

            PropertyInfo propertyInfo = queryable.ElementType.GetProperty(propertyName);
            ParameterExpression parameterExpr = Expression.Parameter(queryable.ElementType, "d");
            Expression selectorExpr = Expression.Lambda(Expression.MakeMemberAccess(parameterExpr, propertyInfo), parameterExpr);
            Type propertyType = propertyInfo.PropertyType;

            // Find matching aggregation method - using reflection
            MethodInfo aggregationMethodInfo = typeof(Queryable).GetMethods()
                .Where(d1 => d1.Name.Equals(aggregationMethod))
                .Select(d2 => new { Method = d2, Parameters = d2.GetParameters() })
                .Where(d3 => d3.Parameters.Length.Equals(2)
                    && d3.Parameters[0].ParameterType.IsGenericType
                    && d3.Parameters[0].ParameterType.GetGenericTypeDefinition().Equals(typeof(IQueryable<>))
                    && d3.Parameters[1].ParameterType.IsGenericType
                    && d3.Parameters[1].ParameterType.GetGenericTypeDefinition().Equals(typeof(Expression<>)))
                .Select(d4 => new { d4.Method, Arguments = d4.Parameters[1].ParameterType.GetGenericArguments() })
                .Where(d5 => d5.Arguments.Length > 0
                    && d5.Arguments[0].IsGenericType
                    && d5.Arguments[0].GetGenericTypeDefinition().Equals(typeof(Func<,>)))
                .Select(d6 => new { d6.Method, Arguments = d6.Arguments[0].GetGenericArguments() })
                .Where(d7 => d7.Arguments.Length > 1
                    && d7.Arguments[0].IsGenericParameter
                    && new[] { "Min", "Max" }.Contains(d7.Method.Name) ? true : d7.Arguments[1].Equals(propertyType))
                .Select(d8 => d8.Method)
                .FirstOrDefault();

            List<Type> genericArguments = new List<Type>();
            genericArguments.Add(queryable.ElementType);
            if (aggregationMethodInfo.GetGenericArguments().Length > 1)
            {
                genericArguments.Add(propertyType);
            }

            MethodCallExpression methodCallExpr = Expression.Call(
                null,
                aggregationMethodInfo.MakeGenericMethod(genericArguments.ToArray()),
                new[] { queryable.Expression, Expression.Quote(selectorExpr) });

            // Call factory method for creating DataServiceOrderedQuery based on Linq expression
            var query = new DataServiceQueryProvider(context).CreateQuery(methodCallExpr);

            Assert.Equal(expectedAggregateUri, query.ToString());
        }
    }
}
