using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Xml;
using Microsoft.OData.Client.Tests.Tracking;
using Microsoft.OData.Edm.Csdl;
using Xunit;

namespace Microsoft.OData.Client.Tests.ALinq
{
    public class CountDistinctTests
    {
        private DataServiceContext context;
        private string serviceUri = "http://example.com";

        #region TestEDMX 

        private const string Edmx =
            @"<edmx:Edmx Version=""4.0"" xmlns:edmx=""http://docs.oasis-open.org/odata/ns/edmx"">
  <edmx:DataServices>
    <Schema Namespace=""Microsoft.OData.Client.Tests.ALinq"" xmlns=""http://docs.oasis-open.org/odata/ns/edm"">
     
      <EntityType Name=""Document"">
        <Key>
          <PropertyRef Name=""Id"" />
        </Key>
        <Property Name=""Id"" Type=""Edm.Int32"" Nullable=""false"" />
        <Property Name=""Name"" Type=""Edm.String"" />
        <Property Name=""FileLength"" Type=""Edm.Int32"" Nullable=""false"" />
      </EntityType>
    </Schema>
    <Schema Namespace=""Default"" xmlns=""http://docs.oasis-open.org/odata/ns/edm"">
      <EntityContainer Name=""Container"">
        <EntitySet Name=""Documents"" EntityType="" Microsoft.OData.Client.Tests.ALinq.Document"" />
      </EntityContainer>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>";

        #endregion

        public CountDistinctTests()
        {
            context = new DataServiceContext(new Uri(serviceUri));
            context.Format.LoadServiceModel = () => CsdlReader.Parse(XmlReader.Create(new StringReader(Edmx)));
            context.Format.UseJson();
        }

        private void InterceptRequestAndMockResponse(DataServiceContext context, string aggregateAlias, object aggregateValue)
        {
            context.Configurations.RequestPipeline.OnMessageCreating = (args) =>
            {
                var contentTypeHeader = "application/json;odata.metadata=minimal;odata.streaming=true;IEEE754Compatible=false;charset=utf-8";

                // e.g. "{\"@odata.context\":\"http://ServiceRoot/$metadata#Numbers(SumIntProp)\",\"value\":[{\"@odata.id\":null,\"SumIntProp\":506}]}"
                var mockedResponse = string.Format(
                    "{{\"@odata.context\":\"{0}/$metadata#{1}({2})\",\"value\":[{{\"@odata.id\":null,\"{2}\":{3}}}]}}",
                    serviceUri,
                    "Document",
                    aggregateAlias,
                    aggregateValue);

                return new CustomizedRequestMessage(
                    args,
                    mockedResponse,
                    new Dictionary<string, string>()
                    {
                        {"Content-Type", contentTypeHeader},
                        {"OData-Version", "4.0"},
                    });
            };
        }

        [Fact]
        public void CountDistinctGetsGeneratedCorrectly()
        {

            var queryable = context.CreateQuery<Document>("Documents");

            // var distinctMethodInfo = typeof(Queryable).GetMethods().First(m => m.Name.Equals("Distinct") && m.GetParameters().Length == 1);
            // List<Type> genericArguments = new List<Type>();
            // genericArguments.Add(queryable.ElementType);
            //
            // var countMethodInfo = typeof(Queryable).GetMethods().First(m => m.Name.Equals("Count") && m.GetParameters().Length == 1);
            //
            // var distinctExpression =
            //     Expression.Call(null, distinctMethodInfo.MakeGenericMethod(genericArguments.ToArray()),new []{queryable.Expression});
            //
            // Expression countDistinctExpression = Expression.Call(null,
            //     countMethodInfo.MakeGenericMethod(genericArguments.ToArray()),
            //     new[] {distinctExpression}
            //   );
            //
            //
            // var query = new DataServiceQueryProvider(context).CreateQuery(countDistinctExpression);
            //
            // // Use reflection to get Execute method - should make it easy to apply different return types
            // var executeMethodInfo = typeof(DataServiceQueryProvider).GetMethods()
            //     .Where(d => d.Name.Equals("Execute")
            //                 && d.IsGenericMethodDefinition
            //                 && d.GetParameters().Length == 1
            //                 && d.GetParameters()[0].ParameterType.Equals(typeof(Expression))
            //                 && d.IsPublic
            //                 && !d.IsStatic
            //     ).FirstOrDefault();
            //
            // var queryProvider = new DataServiceQueryProvider(context);
            // var result = executeMethodInfo.MakeGenericMethod(typeof(int)).Invoke(queryProvider, new object[] { countDistinctExpression });
            InterceptRequestAndMockResponse(context, "CountDistinctName", 100);
            var count = queryable.Select(x => x.Name).Distinct().Count();
        }
    }

    [Key("Id")]
    internal class Document : BaseEntityType
    {
        public int Id { get; set; }

        public string Name { get; set; }
        public int FileLength { get; set; }
    }
}
