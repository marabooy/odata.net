﻿
namespace Microsoft.OData.Client.Tests.Tracking
{
    using Edm.Csdl;
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using System.Xml;
    using Xunit;

    public class DataServiceContextTrackOnlyMleTests
    {
        private Container NoNTrackingContext;
        private Container DefaultTrackingContext;

        #region TestEDMX 

        private const string Edmx =
            @"<edmx:Edmx Version=""4.0"" xmlns:edmx=""http://docs.oasis-open.org/odata/ns/edmx"">
  <edmx:DataServices>
    <Schema Namespace=""Microsoft.OData.Client.Tests.Tracking"" xmlns=""http://docs.oasis-open.org/odata/ns/edm"">
      <EntityType Name=""User"">
        <Key>
          <PropertyRef Name=""Id"" />
        </Key>
        <Property Name=""Id"" Type=""Edm.Int32"" Nullable=""false"" />
        <Property Name=""Name"" Type=""Edm.String"" />
      </EntityType>
      <EntityType Name=""Document"" HasStream=""true"">
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
        <EntitySet Name=""Users"" EntityType=""Microsoft.OData.Client.Tests.Tracking.User"" />
        <EntitySet Name=""Documents"" EntityType=""Microsoft.OData.Client.Tests.Tracking.Document"" />
      </EntityContainer>
    </Schema>
  </edmx:DataServices>
</edmx:Edmx>";

        #endregion


        #region responses

        private const string USERS_RESPONSE = @"{
    ""@odata.context"":""http://localhost:8000/$metadata#Users"",
    ""value"":[
            {""Id"":1,""Name"":""U1""},
            {""Id"":2,""Name"":""U2""},
            {""Id"":3,""Name"":""U3""}
        ]
}
";

        private const string DOCUMENTS_RESPONSE = @"{
  ""@odata.context"": ""https://localhost:8000/$metadata#Documents"",
  ""value"": [
    {
      ""@odata.mediaReadLink"": ""https://localhost:8000/Documents/1/content"",
      ""@odata.mediaContentType"": ""application/pdf"",
      ""Id"": 1,
      ""Name"": ""Sample Doc 1"",
      ""FileLength"": 0
    },
   {
      ""@odata.mediaReadLink"": ""https://localhost:8000/Documents/2/content"",
      ""@odata.mediaContentType"": ""application/pdf"",
      ""Id"": 2,
      ""Name"": ""Sample Doc 2"",
      ""FileLength"": 0
    },
    {
      ""@odata.mediaReadLink"": ""https://localhost:8000/Documents/3/content"",
      ""@odata.mediaContentType"": ""application/pdf"",
      ""Id"": 3,
      ""Name"": ""Sample Doc 3"",
      ""FileLength"": 0
    }
  ]
}";

        private const string DOCUMENT_RESPONSE = @"{
      ""@odata.context"": ""https://localhost:8000/$metadata#Documents/$entity"",
      ""@odata.mediaReadLink"": ""https://localhost:8000/Documents/1/content"",
      ""@odata.mediaContentType"": ""application/pdf"",
      ""Id"": 1,
      ""Name"": ""Sample Doc 1"",
      ""FileLength"": 0
    }";

        private const string USER_RESPONSE = @"{
    ""@odata.context"":""http://localhost:8000/$metadata#Users/$entity"",
    ""Id"":1,
    ""Name"":""U1""
      }";

        #endregion
        private const string AttatchmentResponse = "Hello World!";


        public DataServiceContextTrackOnlyMleTests()
        {
            var uri = new Uri("http://localhost:8000");

            NoNTrackingContext = new Container(uri)
            {
                MergeOption = MergeOption.NoTracking
            };

            DefaultTrackingContext = new Container(uri);
        }

        [Fact]
        public void BehaviourShouldMatchNoTrackingForFetchedItemsNonMLE()
        {
            SetupContextWithRequestPipeline(new DataServiceContext[] { DefaultTrackingContext, NoNTrackingContext }, false);

            var users = DefaultTrackingContext.Users.ExecuteAsync().GetAwaiter().GetResult();
            var users2 = NoNTrackingContext.Users.ExecuteAsync().GetAwaiter().GetResult();
            // data should be fetched and the count should be the same
            Assert.Equal(users.ToList().Count, users2.ToList().Count);

            //  entities should be tracked by no tracking context
            Assert.Equal(0, NoNTrackingContext.EntityTracker.Entities.ToList().Count);
            Assert.Equal(3, DefaultTrackingContext.EntityTracker.Entities.ToList().Count);
            Assert.Equal(0, NoNTrackingContext.Entities.ToList().Count);
            Assert.Equal(3, DefaultTrackingContext.Entities.ToList().Count);
        }
        [Fact]
        public void TrackMediaEntitiesShouldBePopulatedWithMediaEntities()
        {
            SetupContextWithRequestPipeline(new DataServiceContext[] { DefaultTrackingContext, NoNTrackingContext }, true);

            var mLeItems = DefaultTrackingContext.Documents.ExecuteAsync().GetAwaiter().GetResult().ToList();
            var mleNoTracking = NoNTrackingContext.Documents.ExecuteAsync().GetAwaiter().GetResult().ToList();
            // data should be equals
            Assert.Equal(mleNoTracking.Count, mLeItems.Count);

            Assert.Equal(0, NoNTrackingContext.EntityTracker.Entities.ToList().Count);
            Assert.Equal(mLeItems.Count, DefaultTrackingContext.Entities.ToList().Count);
            var entityDescriptors = DefaultTrackingContext.EntityTracker.Entities.ToList();
            Assert.Equal(mLeItems.Count, entityDescriptors.Count);

            foreach (var entityDescriptor in entityDescriptors)
            {
                Assert.Equal(EntityStates.Unchanged, entityDescriptor.State);
                Assert.NotNull(entityDescriptor.ReadStreamUri);
            }

            // verify that the stream links are equal 
            foreach (var document in mleNoTracking)
            {
                var doc = document as BaseEntityType;
                Assert.NotNull(doc.StreamDescriptor.SelfLink);
                Assert.NotNull(NoNTrackingContext.GetReadStreamUri(document));

                //try and get the content and verify that the content matches the values
                Stream result = GetTestReadStreamResult(NoNTrackingContext, document).GetAwaiter().GetResult();

                Assert.Equal("Hello World!", new StreamReader(result).ReadToEnd());

            }





        }

        private async Task<Stream> GetTestReadStreamResult(DataServiceContext dataServiceContext, Document document)
        {
            dataServiceContext.Configurations.RequestPipeline.OnMessageCreating = args =>
            {
                // if we read the wrong link then reply with gibberish
                var resp = (args.RequestUri == document.StreamDescriptor.SelfLink) ? AttatchmentResponse : "gibberish";

                return new CustomizedRequestMessage(
                    args,
                    resp,
                    new Dictionary<string, string>()
                    {
                            {"Content-Type", "text/plain"},

                    });


            };


            var streamResponse = await dataServiceContext.GetReadStreamAsync(document, new DataServiceRequestArgs());
            return streamResponse.Stream;

        }

        [Theory]
        [InlineData(false, 1, 1)]
        public async void TestAddingNewItemsBehaviourShouldBeUnAltered(bool isMle, int expectedNoTracking,
            int expectedTrackingMle)
        {
            SetupContextWithRequestPipelineForSaving(
                new DataServiceContext[] { NoNTrackingContext, DefaultTrackingContext }, isMle);
            var document = new Document
            {
                Id = 1,
                FileLength = 0,
                Name = "New Document"
            };

            var user = new User
            {
                Name = "Some name"
            };
            if (isMle)
            {

                DefaultTrackingContext.AddObject("Documents", document);

                DefaultTrackingContext.SetSaveStream(document, new MemoryStream(new byte[] { 64, 65, 66 }), true, "image/png", "UnitTestLogo.png");

                NoNTrackingContext.AddObject("Documents", document);
                NoNTrackingContext.SetSaveStream(document, new MemoryStream(new byte[] { 64, 65, 66 }), true, "image/png", "UnitTestLogo.png");

            }
            else
            {
                DefaultTrackingContext.AddObject("Users", user);
                NoNTrackingContext.AddObject("Users", user);
                Assert.NotNull(NoNTrackingContext.GetEntityDescriptor(user));
                Assert.NotNull(DefaultTrackingContext.GetEntityDescriptor(user));

            }



            await SaveContextChanges(new DataServiceContext[] { DefaultTrackingContext, NoNTrackingContext });

            Assert.Equal(expectedTrackingMle, DefaultTrackingContext.Entities.ToList().Count);
            Assert.Equal(expectedNoTracking, NoNTrackingContext.Entities.ToList().Count);

        }

        private async Task SaveContextChanges(DataServiceContext[] dataServiceContexts)
        {
            foreach (var dataServiceContext in dataServiceContexts)
            {
                await dataServiceContext.SaveChangesAsync();
            }
        }

        private void SetupContextWithRequestPipelineForSaving(DataServiceContext[] dataServiceContexts, bool forMle)
        {
            var response = forMle ? DOCUMENT_RESPONSE : USER_RESPONSE;
            var location = forMle ? "http://localhost:8000/Documents(1)/edit/content" : "http://localhost:8000/Users(1)";

            foreach (var context in dataServiceContexts)
            {
                context.Configurations.RequestPipeline.OnMessageCreating =
                    (args) => new CustomizedRequestMessage(
                        args,
                        response,
                        new Dictionary<string, string>()
                        {
                            {"Content-Type", "application/json;charset=utf-8"},
                            { "Location", location },

                        });
            }

        }


        private void SetupContextWithRequestPipeline(DataServiceContext[] dataServiceContexts, bool forMle)
        {

            var response = forMle ? DOCUMENTS_RESPONSE : USERS_RESPONSE;

            foreach (var context in dataServiceContexts)
            {
                context.Configurations.RequestPipeline.OnMessageCreating =
                    (args) => new CustomizedRequestMessage(
                        args,
                        response,
                        new Dictionary<string, string>()
                        {
                                {"Content-Type", "application/json;charset=utf-8"},
                        });
            }

        }


        class Container : DataServiceContext
        {

            public Container(global::System.Uri serviceRoot) :
                base(serviceRoot, ODataProtocolVersion.V4)
            {
                this.Format.LoadServiceModel = () => CsdlReader.Parse(XmlReader.Create(new StringReader(Edmx)));
                this.Format.UseJson();
                this.Users = base.CreateQuery<User>("Users");
                this.Documents = base.CreateQuery<Document>("Documents");
            }

            public DataServiceQuery<User> Users { get; private set; }
            public DataServiceQuery<Document> Documents { get; private set; }
        }
    }
    [Key("Id")]
    [HasStream()]
    internal class Document : BaseEntityType
    {
        public int Id { get; set; }

        public string Name { get; set; }
        public int FileLength { get; set; }
    }
    [Key("Id")]
    internal class User : BaseEntityType
    {
        public int Id { get; set; }
        public string Name { get; set; }
    }

    public class CustomizedRequestMessage : HttpWebRequestMessage
    {
        public string Response { get; set; }
        public Dictionary<string, string> CustomizedHeaders { get; set; }

        public CustomizedRequestMessage(DataServiceClientRequestMessageArgs args)
            : base(args)
        {
        }

        public CustomizedRequestMessage(DataServiceClientRequestMessageArgs args, string response, Dictionary<string, string> headers)
            : base(args)
        {
            this.Response = response;
            this.CustomizedHeaders = headers;
        }

        public IODataResponseMessage GetResponse()
        {
            return new HttpWebResponseMessage(
                this.CustomizedHeaders,
                200,
                () =>
                {
                    byte[] byteArray = Encoding.UTF8.GetBytes(this.Response);
                    return new MemoryStream(byteArray);
                });
        }


        public override IAsyncResult BeginGetResponse(AsyncCallback callback, object state)
        {

            var tcs = new TaskCompletionSource<bool>(state);
            tcs.TrySetResult(true);
            callback(tcs.Task);
            return tcs.Task;
        }

        public override IODataResponseMessage EndGetResponse(IAsyncResult asyncResult)
        {
            System.Threading.Thread.Sleep(1000);
            return GetResponse();
        }
    }

}