//---------------------------------------------------------------------
// <copyright file="DollarApplyTests.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
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
        private Random rand = new Random();
        private readonly DataServiceContext dsContext;
        private const string serviceUri = "http://tempuri.org";
        private const string numbersEntitySetName = "Numbers";
        private static string aggregateUriTemplate = serviceUri + '/' + numbersEntitySetName + "?$apply=aggregate({0} with {1} as {2})";

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

                    testData.Add(new object[]
                    {
                        aggregationMethod,
                        aggregationAlias,
                        propertyName,
                        string.Format(aggregateUriTemplate, propertyName, aggregationMethodToLower, aggregationAlias)
                    });
                    testData.Add(new object[]
                    { 
                        aggregationMethod,
                        nullableAggregationAlias,
                        nullablePropertyName,
                        string.Format(aggregateUriTemplate, nullablePropertyName, aggregationMethodToLower, nullableAggregationAlias) 
                    });
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

            dsContext = new DataServiceContext(new Uri(serviceUri));
            dsContext.Format.UseJson(model);
        }

        [Theory]
        [MemberData(nameof(GetApplyQueryOptionData))]
        public void TranslateLinqAggregateExpressionToExpectedUriThenExecute(string aggregationMethod, string aggregationAlias, string propertyName, string expectedAggregateUri)
        {
            DataServiceQuery<Number> queryable = this.dsContext.CreateQuery<Number>(numbersEntitySetName);

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
            var query = new DataServiceQueryProvider(dsContext).CreateQuery(methodCallExpr);

            // Verify expected aggregate Uri
            Assert.Equal(expectedAggregateUri, query.ToString());

            Type returnType = propertyType;
            if ((propertyType.Equals(typeof(int)) || propertyType.Equals(typeof(long))) 
                && aggregationMethod.Equals("Average", StringComparison.OrdinalIgnoreCase))
            {
                returnType = aggregationMethodInfo.ReturnType;
            }

            // Execute expression and verify result
            var randomAggregateValue = GenerateRandomAggregateValue(aggregationMethod, returnType);
            InterceptRequestAndMockResponse(aggregationAlias, randomAggregateValue);

            // Use reflection to get Execute method - should make it easy to apply different return types
            MethodInfo executeMethodInfo = typeof(DataServiceQueryProvider).GetMethods()
                .Where(d => d.Name.Equals("Execute")
                    && d.IsGenericMethodDefinition
                    && d.GetParameters().Length == 1
                    && d.GetParameters()[0].ParameterType.Equals(typeof(Expression))
                    && d.IsPublic
                    && !d.IsStatic
                ).FirstOrDefault();

            var dsqpInstance = new DataServiceQueryProvider(dsContext);
            var result = executeMethodInfo.MakeGenericMethod(returnType).Invoke(dsqpInstance, new object[] { methodCallExpr });

            Assert.Equal(result, randomAggregateValue);
        }

        // To generate a relevant aggregate value to be returned in the mock response
        private object GenerateRandomAggregateValue(string aggregationMethod, Type returnType)
        {
            int lowerBound = 100;
            int upperBound = 1000;
            object aggregationValue;

            switch (aggregationMethod.ToLowerInvariant())
            {
                case "average":
                    // A decimal value should suffice as average for all types
                    aggregationValue = Math.Round((double)upperBound * rand.NextDouble(), 2);
                    break;
                case "min":
                case "max":
                case "sum":
                    if (returnType.Equals(typeof(int)) || returnType.Equals(typeof(long)))
                        aggregationValue = rand.Next(lowerBound, upperBound);
                    else
                        aggregationValue = Math.Round(upperBound * rand.NextDouble(), 2);
                    break;
                default:
                    aggregationValue = rand.Next(lowerBound, upperBound);
                    break;
            }

            // Get underlying type if type is nullable - to use in Convert.ChangeType
            Type underlyingType = Nullable.GetUnderlyingType(returnType);
            if (underlyingType == null) // Not a nullable type
            {
                underlyingType = returnType;
            }

            return Convert.ChangeType(aggregationValue, underlyingType, CultureInfo.InvariantCulture.NumberFormat);
        }

        private void InterceptRequestAndMockResponse(string aggregateAlias, object aggregateValue)
        {
            dsContext.Configurations.RequestPipeline.OnMessageCreating = (args) =>
            {
                var contentTypeHeader = "application/json;odata.metadata=minimal;odata.streaming=true;IEEE754Compatible=false;charset=utf-8";
                var odataVersionHeader = "4.0";
                // e.g. "{\"@odata.context\":\"http://ServiceRoot/$metadata#Numbers(SumIntProp)\",\"value\":[{\"@odata.id\":null,\"SumIntProp\":506}]}"
                var mockedResponse = string.Format(
                    "{{\"@odata.context\":\"{0}/$metadata#{1}({2})\",\"value\":[{{\"@odata.id\":null,\"{2}\":{3}}}]}}",
                    serviceUri,
                    numbersEntitySetName,
                    aggregateAlias,
                    aggregateValue);

                return new TestHttpWebRequestMessage(args,
                    new Dictionary<string, string>
                    {
                        {"Content-Type", contentTypeHeader},
                        {"OData-Version", odataVersionHeader},
                    },
                    () => new MemoryStream(Encoding.UTF8.GetBytes(mockedResponse)));
            };
        }
    }
}
