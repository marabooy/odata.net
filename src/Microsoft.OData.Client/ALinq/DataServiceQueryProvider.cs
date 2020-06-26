//---------------------------------------------------------------------
// <copyright file="DataServiceQueryProvider.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

namespace Microsoft.OData.Client
{
    #region Namespaces

    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Linq;
    using System.Linq.Expressions;
    using System.Reflection;
    using Newtonsoft.Json.Linq;

    #endregion Namespaces

    /// <summary>
    /// QueryProvider implementation
    /// </summary>
    public sealed class DataServiceQueryProvider : IQueryProvider
    {
        /// <summary>DataServiceContext for query provider</summary>
        internal readonly DataServiceContext Context;

        /// <summary>Constructs a query provider based on the context passed in </summary>
        /// <param name="context">The context for the query provider</param>
        internal DataServiceQueryProvider(DataServiceContext context)
        {
            this.Context = context;
        }

        #region IQueryProvider implementation

        /// <summary>Factory method for creating DataServiceOrderedQuery based on expression </summary>
        /// <param name="expression">The expression for the new query</param>
        /// <returns>new DataServiceQuery</returns>
        public IQueryable CreateQuery(Expression expression)
        {
            Util.CheckArgumentNull(expression, "expression");
            Type et = TypeSystem.GetElementType(expression.Type);
            Type qt = typeof(DataServiceQuery<>.DataServiceOrderedQuery).MakeGenericType(et);
            object[] args = new object[] { expression, this };

            ConstructorInfo ci = qt.GetInstanceConstructor(
                false /*isPublic*/,
                new Type[] { typeof(Expression), typeof(DataServiceQueryProvider) });

            return (IQueryable)Util.ConstructorInvoke(ci, args);
        }

        /// <summary>Factory method for creating DataServiceOrderedQuery based on expression </summary>
        /// <typeparam name="TElement">generic type</typeparam>
        /// <param name="expression">The expression for the new query</param>
        /// <returns>new DataServiceQuery</returns>
        public IQueryable<TElement> CreateQuery<TElement>(Expression expression)
        {
            Util.CheckArgumentNull(expression, "expression");
            return new DataServiceQuery<TElement>.DataServiceOrderedQuery(expression, this);
        }

        /// <summary>Creates and executes a DataServiceQuery based on the passed in expression</summary>
        /// <param name="expression">The expression for the new query</param>
        /// <returns>the results</returns>
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining | System.Runtime.CompilerServices.MethodImplOptions.NoOptimization)]
        public object Execute(Expression expression)
        {
            Util.CheckArgumentNull(expression, "expression");

            MethodInfo mi = typeof(DataServiceQueryProvider).GetMethod("ReturnSingleton", false /*isPublic*/, false /*isStatic*/);
            return mi.MakeGenericMethod(expression.Type).Invoke(this, new object[] { expression });
        }

        /// <summary>Creates and executes a DataServiceQuery based on the passed in expression</summary>
        /// <typeparam name="TResult">generic type</typeparam>
        /// <param name="expression">The expression for the new query</param>
        /// <returns>the results</returns>
        public TResult Execute<TResult>(Expression expression)
        {
            Util.CheckArgumentNull(expression, "expression");
            return ReturnSingleton<TResult>(expression);
        }

        #endregion

        /// <summary>Creates and executes a DataServiceQuery based on the passed in expression which results a single value</summary>
        /// <typeparam name="TElement">generic type</typeparam>
        /// <param name="expression">The expression for the new query</param>
        /// <returns>single valued results</returns>
        internal TElement ReturnSingleton<TElement>(Expression expression)
        {
            IQueryable<TElement> query = new DataServiceQuery<TElement>.DataServiceOrderedQuery(expression, this);

            MethodCallExpression mce = expression as MethodCallExpression;
            Debug.Assert(mce != null, "mce != null");

            Func<string, object> aggregationParseFunction = (response) =>
            {
                JObject obj = JObject.Parse(response);
                JToken contextToken, valueToken;
                if (obj.TryGetValue(XmlConstants.ODataContext, out contextToken) && obj.TryGetValue("value", out valueToken))
                {
                    string contextValue = contextToken.ToString();
                    JArray valueArray = valueToken as JArray;

                    // To get the alias for the aggregation
                    int leftParenPos = contextValue.LastIndexOf(UriHelper.LEFTPAREN);
                    int rightParenPos = contextValue.LastIndexOf(UriHelper.RIGHTPAREN);

                    if (leftParenPos > 0 && rightParenPos > leftParenPos && valueArray != null)
                    {
                        string alias = contextValue.Substring(leftParenPos + 1, (rightParenPos - leftParenPos - 1));
                        object aggregationResult = valueArray.Count > 0 ? valueArray[0].Value<string>(alias) : null;

                        Type underlyingType = Nullable.GetUnderlyingType(typeof(TElement));
                        if (underlyingType == null) // Not a nullable type
                        {
                            underlyingType = typeof(TElement);
                        }

                        return Convert.ChangeType(aggregationResult, underlyingType, System.Globalization.CultureInfo.InvariantCulture.NumberFormat);
                    }
                }

                return null;
            };

            if (TryAnalyzeCountDistinct(mce))
            {
                return ((DataServiceQuery<TElement>)query).GetValue<TElement>(this.Context, aggregationParseFunction);
            }

            SequenceMethod sequenceMethod;
            if (ReflectionUtil.TryIdentifySequenceMethod(mce.Method, out sequenceMethod))
            {
                switch (sequenceMethod)
                {
                    case SequenceMethod.Single:
                        return query.AsEnumerable().Single();
                    case SequenceMethod.SingleOrDefault:
                        return query.AsEnumerable().SingleOrDefault();
                    case SequenceMethod.First:
                        return query.AsEnumerable().First();
                    case SequenceMethod.FirstOrDefault:
                        return query.AsEnumerable().FirstOrDefault();
                    case SequenceMethod.LongCount:
                    case SequenceMethod.Count:
                        {
                            Func<string, object> parseResponseFunc = (response) =>
                            {
                                return Convert.ChangeType(response, typeof(TElement), System.Globalization.CultureInfo.InvariantCulture.NumberFormat);
                            };
                            return ((DataServiceQuery<TElement>)query).GetValue<TElement>(this.Context, parseResponseFunc);
                        }
                    case SequenceMethod.SumIntSelector:
                    case SequenceMethod.SumDoubleSelector:
                    case SequenceMethod.SumDecimalSelector:
                    case SequenceMethod.SumLongSelector:
                    case SequenceMethod.SumSingleSelector:
                    case SequenceMethod.SumNullableIntSelector:
                    case SequenceMethod.SumNullableDoubleSelector:
                    case SequenceMethod.SumNullableDecimalSelector:
                    case SequenceMethod.SumNullableLongSelector:
                    case SequenceMethod.SumNullableSingleSelector:
                    case SequenceMethod.AverageIntSelector:
                    case SequenceMethod.AverageDoubleSelector:
                    case SequenceMethod.AverageDecimalSelector:
                    case SequenceMethod.AverageLongSelector:
                    case SequenceMethod.AverageSingleSelector:
                    case SequenceMethod.AverageNullableIntSelector:
                    case SequenceMethod.AverageNullableDoubleSelector:
                    case SequenceMethod.AverageNullableDecimalSelector:
                    case SequenceMethod.AverageNullableLongSelector:
                    case SequenceMethod.AverageNullableSingleSelector:
                    case SequenceMethod.MinSelector:
                    case SequenceMethod.MinDoubleSelector:
                    case SequenceMethod.MinDecimalSelector:
                    case SequenceMethod.MinLongSelector:
                    case SequenceMethod.MinSingleSelector:
                    case SequenceMethod.MinNullableIntSelector:
                    case SequenceMethod.MinNullableDoubleSelector:
                    case SequenceMethod.MinNullableDecimalSelector:
                    case SequenceMethod.MinNullableLongSelector:
                    case SequenceMethod.MinNullableSingleSelector:
                    case SequenceMethod.MaxSelector:
                    case SequenceMethod.MaxDoubleSelector:
                    case SequenceMethod.MaxDecimalSelector:
                    case SequenceMethod.MaxLongSelector:
                    case SequenceMethod.MaxSingleSelector:
                    case SequenceMethod.MaxNullableIntSelector:
                    case SequenceMethod.MaxNullableDoubleSelector:
                    case SequenceMethod.MaxNullableDecimalSelector:
                    case SequenceMethod.MaxNullableLongSelector:
                    case SequenceMethod.MaxNullableSingleSelector:
                        {
                            return ((DataServiceQuery<TElement>)query).GetValue<TElement>(this.Context, aggregationParseFunction);
                        }
                    default:
                        throw Error.MethodNotSupported(mce);
                }
            }

            // Should never get here - should be caught by expression compiler.
            Debug.Assert(false, "Not supported singleton operator not caught by Resource Binder");
            throw Error.MethodNotSupported(mce);
        }

        private bool TryAnalyzeCountDistinct(MethodCallExpression mce)
        {

            // Since this is in the return path of the countdistinct we need to check for correct method sequence only and not validate anything else
            // todo refactor the validation
            var nextSequence = mce.Arguments.Any() ? mce.Arguments[0] as MethodCallExpression : null;
            // Validate the next call sequence is .Distinct().Count() else return false;
            // Next we validate we are seeing a token for Distinct
            if (nextSequence == null) return false;


            SequenceMethod nextMethodToken;
            ReflectionUtil.TryIdentifySequenceMethod(nextSequence.Method, out nextMethodToken);
            if (nextMethodToken != SequenceMethod.Distinct)
            {
                return false;
            }

            nextSequence = nextSequence.Arguments.Any() ? nextSequence.Arguments[0] as MethodCallExpression : null;
            if (nextSequence == null) return false;

            ReflectionUtil.TryIdentifySequenceMethod(nextSequence.Method, out nextMethodToken);
            if (nextMethodToken != SequenceMethod.Select)
            {
                return false;
            }

            return true;
        }

        /// <summary>Builds the Uri for the expression passed in.</summary>
        /// <param name="e">The expression to translate into a Uri</param>
        /// <returns>Query components</returns>
        internal QueryComponents Translate(Expression e)
        {
            Uri uri;
            Version version;
            bool addTrailingParens = false;
            Dictionary<Expression, Expression> normalizerRewrites = null;

            // short cut analysis if just a resource set or singleton resource.
            // note - to be backwards compatible with V1, will only append trailing () for queries
            // that include more then just a resource set.
            if (!(e is QueryableResourceExpression))
            {
                normalizerRewrites = new Dictionary<Expression, Expression>(ReferenceEqualityComparer<Expression>.Instance);
                e = Evaluator.PartialEval(e);
                //todo update and remove pattern where method name 
                e = ExpressionNormalizer.Normalize(e, normalizerRewrites);
                e = ResourceBinder.Bind(e, this.Context);
                addTrailingParens = true;
            }

            UriWriter.Translate(this.Context, addTrailingParens, e, out uri, out version);
            ResourceExpression re = e as ResourceExpression;
            Type lastSegmentType = re.Projection == null ? re.ResourceType : re.Projection.Selector.Parameters[0].Type;
            LambdaExpression selector = re.Projection == null ? null : re.Projection.Selector;
            return new QueryComponents(uri, version, lastSegmentType, selector, normalizerRewrites);
        }
    }
}
