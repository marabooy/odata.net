﻿//---------------------------------------------------------------------
// <copyright file="ODataMultipartMixedBatchReader.cs" company="Microsoft">
//      Copyright (C) Microsoft Corporation. All rights reserved. See License.txt in the project root for license information.
// </copyright>
//---------------------------------------------------------------------

using System.IO;

namespace Microsoft.OData.MultipartMixed
{
    #region Namespaces
    using System;
    using System.Diagnostics;
    using System.Diagnostics.CodeAnalysis;
    using System.Text;

#if PORTABLELIB
    using System.Threading.Tasks;
#endif
    #endregion Namespaces

    internal sealed class ODataMultipartMixedBatchReader : ODataBatchReader
    {
        /// <summary>The batch stream used by the batch reader to divide a batch payload into parts.</summary>
        private readonly ODataMultipartMixedBatchReaderStream batchStream;

        /// <summary>
        /// Gets the reader's input context as the real runtime type.
        /// </summary>
        private readonly ODataRawInputContext rawInputContext;

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="inputContext">The input context to read the content from.</param>
        /// <param name="batchBoundary">The boundary string for the batch structure itself.</param>
        /// <param name="batchEncoding">The encoding to use to read from the batch stream.</param>
        /// <param name="synchronous">true if the reader is created for synchronous operation; false for asynchronous.</param>
        internal ODataMultipartMixedBatchReader(ODataRawInputContext inputContext, string batchBoundary, Encoding batchEncoding, bool synchronous)
            : base(inputContext, synchronous)
        {
            Debug.Assert(inputContext != null, "inputContext != null");
            Debug.Assert(!string.IsNullOrEmpty(batchBoundary), "!string.IsNullOrEmpty(batchBoundary)");

            this.rawInputContext = inputContext;
            this.batchStream = new ODataMultipartMixedBatchReaderStream(this.rawInputContext, batchBoundary, batchEncoding);
        }

        /// <summary>
        /// Returns the cached <see cref="ODataBatchOperationRequestMessage"/> for reading the content of an operation
        /// in a batch request.
        /// </summary>
        /// <returns>The message that can be used to read the content of the batch request operation from.</returns>
        protected override ODataBatchOperationRequestMessage CreateOperationRequestMessageImplementation()
        {
            string requestLine = this.batchStream.ReadFirstNonEmptyLine();

            string httpMethod;
            Uri requestUri;
            this.ParseRequestLine(requestLine, out httpMethod, out requestUri);

            // Read all headers and create the request message
            ODataBatchOperationHeaders headers = this.batchStream.ReadHeaders();

            if (this.batchStream.ChangeSetBoundary != null)
            {
                RecordContentId(headers, true);
            }

            ODataBatchOperationRequestMessage requestMessage = BuildOperationRequestMessage(
                () => ODataBatchUtils.CreateBatchOperationReadStream(this.batchStream, headers, this),
                httpMethod,
                requestUri,
                headers);

            return requestMessage;
        }

        /// <summary>
        /// Returns the cached <see cref="ODataBatchOperationRequestMessage"/> for reading the content of an operation
        /// in a batch request.
        /// </summary>
        /// <returns>The message that can be used to read the content of the batch request operation from.</returns>
        protected override ODataBatchOperationResponseMessage CreateOperationResponseMessageImplementation()
        {
            string responseLine = this.batchStream.ReadFirstNonEmptyLine();

            int statusCode = this.ParseResponseLine(responseLine);

            // Read all headers and create the response message
            ODataBatchOperationHeaders headers = this.batchStream.ReadHeaders();

            if (this.batchStream.ChangeSetBoundary != null)
            {
                RecordContentId(headers, false);
            }

            // In responses we don't need to use our batch URL resolver, since there are no cross referencing URLs
            // so use the URL resolver from the batch message instead.
            ODataBatchOperationResponseMessage responseMessage = BuildOperationResponseMessage(
                () => ODataBatchUtils.CreateBatchOperationReadStream(this.batchStream, headers, this),
                statusCode,
                headers);

            //// NOTE: Content-IDs for cross referencing are only supported in request messages; in responses
            ////       we allow a Content-ID header but don't process it (i.e., don't add the content ID to the URL resolver).

            return responseMessage;
        }

        /// <summary>
        /// Implementation of the reader logic when in state 'Start'.
        /// </summary>
        /// <returns>The batch reader state after the read.</returns>
        protected override ODataBatchReaderState ReadAtStartImplementation()
        {
            return this.SkipToNextPartAndReadHeaders();
        }

        /// <summary>
        /// Implementation of the reader logic when in state 'Operation'.
        /// </summary>
        /// <returns>The batch reader state after the read.</returns>
        protected override ODataBatchReaderState ReadAtOperationImplementation()
        {
            return this.SkipToNextPartAndReadHeaders();
        }

        /// <summary>
        /// Implementation of the reader logic when in state 'ChangesetStart'.
        /// </summary>
        /// <returns>The batch reader state after the read.</returns>
        protected override ODataBatchReaderState ReadAtChangesetStartImplementation()
        {
            return this.SkipToNextPartAndReadHeaders();
        }

        /// <summary>
        /// Implementation of the reader logic when in state 'ChangesetEnd'.
        /// </summary>
        /// <returns>The batch reader state after the read.</returns>
        protected override ODataBatchReaderState ReadAtChangesetEndImplementation()
        {
            return this.SkipToNextPartAndReadHeaders();
        }

        /// <summary>
        /// Validate the changeset boundary is setup properly in the reader stream.
        /// </summary>
        protected override void VerifyReaderStreamChangesetStartImplementation()
        {
            if (this.batchStream.ChangeSetBoundary == null)
            {
                ThrowODataException(Strings.ODataBatchReader_ReaderStreamChangesetBoundaryCannotBeNull);
            }
        }

        /// <summary>
        /// Reset the changeset boundary in the reader stream at the end of the changeset processing.
        /// </summary>
        protected override void ResetReaderStreamChangesetBoundary()
        {
            this.batchStream.ResetChangeSetBoundary();
        }

        /// <summary>
        /// Returns the next state of the batch reader after an end boundary has been found.
        /// </summary>
        /// <returns>The next state of the batch reader.</returns>
        private ODataBatchReaderState GetEndBoundaryState()
        {
            switch (this.State)
            {
                case ODataBatchReaderState.Initial:
                    // If we find an end boundary when in state 'Initial' it means that we
                    // have an empty batch. The next state will be 'Completed'.
                    return ODataBatchReaderState.Completed;

                case ODataBatchReaderState.Operation:
                    // If we find an end boundary in state 'Operation' we have finished
                    // processing an operation and found the end boundary of either the
                    // current changeset or the batch.
                    return this.batchStream.ChangeSetBoundary == null
                        ? ODataBatchReaderState.Completed
                        : ODataBatchReaderState.ChangesetEnd;

                case ODataBatchReaderState.ChangesetStart:
                    // If we find an end boundary when in state 'ChangeSetStart' it means that
                    // we have an empty changeset. The next state will be 'ChangeSetEnd'
                    return ODataBatchReaderState.ChangesetEnd;

                case ODataBatchReaderState.ChangesetEnd:
                    // If we are at the end of a changeset and find an end boundary
                    // we reached the end of the batch
                    return ODataBatchReaderState.Completed;

                case ODataBatchReaderState.Completed:
                    // We should never get here when in Completed state.
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataBatchReader_GetEndBoundary_Completed));

                case ODataBatchReaderState.Exception:
                    // We should never get here when in Exception state.
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataBatchReader_GetEndBoundary_Exception));

                default:
                    // Invalid enum value
                    throw new ODataException(Strings.General_InternalError(InternalErrorCodes.ODataBatchReader_GetEndBoundary_UnknownValue));
            }
        }

        /// <summary>
        /// Parses the request line of a batch operation request.
        /// </summary>
        /// <param name="requestLine">The request line as a string.</param>
        /// <param name="httpMethod">The parsed HTTP method of the request.</param>
        /// <param name="requestUri">The parsed <see cref="Uri"/> of the request.</param>
        private void ParseRequestLine(string requestLine, out string httpMethod, out Uri requestUri)
        {
            Debug.Assert(!this.rawInputContext.ReadingResponse, "Must only be called for requests.");

            // Batch Request: POST /Customers HTTP/1.1
            // Since the uri can contain spaces, the only way to read the request url, is to
            // check for first space character and last space character and anything between
            // them.
            int firstSpaceIndex = requestLine.IndexOf(' ');

            // Check whether there are enough characters after the first space for the 2nd and 3rd segments
            // (and a whitespace in between)
            if (firstSpaceIndex <= 0 || requestLine.Length - 3 <= firstSpaceIndex)
            {
                // only 1 segment or empty first segment or not enough left for 2nd and 3rd segments
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidRequestLine(requestLine));
            }

            int lastSpaceIndex = requestLine.LastIndexOf(' ');
            if (lastSpaceIndex < 0 || lastSpaceIndex - firstSpaceIndex - 1 <= 0 || requestLine.Length - 1 <= lastSpaceIndex)
            {
                // only 2 segments or empty 2nd or 3rd segments
                // only 1 segment or empty first segment or not enough left for 2nd and 3rd segments
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidRequestLine(requestLine));
            }

            httpMethod = requestLine.Substring(0, firstSpaceIndex);               // Request - Http method
            string uriSegment = requestLine.Substring(firstSpaceIndex + 1, lastSpaceIndex - firstSpaceIndex - 1);      // Request - Request uri
            string httpVersionSegment = requestLine.Substring(lastSpaceIndex + 1);             // Request - Http version

            // Validate HttpVersion
            if (string.CompareOrdinal(ODataConstants.HttpVersionInBatching, httpVersionSegment) != 0)
            {
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidHttpVersionSpecified(httpVersionSegment, ODataConstants.HttpVersionInBatching));
            }

            // NOTE: this method will throw if the method is not recognized.
            HttpUtils.ValidateHttpMethod(httpMethod);

            // Validate the HTTP method when reading a request
            if (this.batchStream.ChangeSetBoundary != null)
            {
                // allow all methods except for GET
                if (HttpUtils.IsQueryMethod(httpMethod))
                {
                    throw new ODataException(Strings.ODataBatch_InvalidHttpMethodForChangeSetRequest(httpMethod));
                }
            }

            requestUri = new Uri(uriSegment, UriKind.RelativeOrAbsolute);
        }

        /// <summary>
        /// Parses the response line of a batch operation response.
        /// </summary>
        /// <param name="responseLine">The response line as a string.</param>
        /// <returns>The parsed status code from the response line.</returns>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "'this' is used when built in debug")]
        private int ParseResponseLine(string responseLine)
        {
            Debug.Assert(this.rawInputContext.ReadingResponse, "Must only be called for responses.");

            // Batch Response: HTTP/1.1 200 Ok
            // Since the http status code strings have spaces in them, we cannot use the same
            // logic. We need to check for the second space and anything after that is the error
            // message.
            int firstSpaceIndex = responseLine.IndexOf(' ');
            if (firstSpaceIndex <= 0 || responseLine.Length - 3 <= firstSpaceIndex)
            {
                // only 1 segment or empty first segment or not enough left for 2nd and 3rd segments
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidResponseLine(responseLine));
            }

            int secondSpaceIndex = responseLine.IndexOf(' ', firstSpaceIndex + 1);
            if (secondSpaceIndex < 0 || secondSpaceIndex - firstSpaceIndex - 1 <= 0 || responseLine.Length - 1 <= secondSpaceIndex)
            {
                // only 2 segments or empty 2nd or 3rd segments
                // only 1 segment or empty first segment or not enough left for 2nd and 3rd segments
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidResponseLine(responseLine));
            }

            string httpVersionSegment = responseLine.Substring(0, firstSpaceIndex);
            string statusCodeSegment = responseLine.Substring(firstSpaceIndex + 1, secondSpaceIndex - firstSpaceIndex - 1);

            // Validate HttpVersion
            if (string.CompareOrdinal(ODataConstants.HttpVersionInBatching, httpVersionSegment) != 0)
            {
                throw new ODataException(Strings.ODataBatchReaderStream_InvalidHttpVersionSpecified(httpVersionSegment, ODataConstants.HttpVersionInBatching));
            }

            int intResult;
            if (!Int32.TryParse(statusCodeSegment, out intResult))
            {
                throw new ODataException(Strings.ODataBatchReaderStream_NonIntegerHttpStatusCode(statusCodeSegment));
            }

            return intResult;
        }

        /// <summary>
        /// Skips all data in the stream until the next part is detected; then reads the part's request/response line and headers.
        /// </summary>
        /// <returns>The next state of the batch reader after skipping to the next part and reading the part's beginning.</returns>
        private ODataBatchReaderState SkipToNextPartAndReadHeaders()
        {
            bool isEndBoundary, isParentBoundary;
            bool foundBoundary = this.batchStream.SkipToBoundary(out isEndBoundary, out isParentBoundary);

            if (!foundBoundary)
            {
                // We did not find the expected boundary at all in the payload;
                // we are done reading. Depending on where we are report changeset end or completed state
                if (this.batchStream.ChangeSetBoundary == null)
                {
                    return ODataBatchReaderState.Completed;
                }
                else
                {
                    return ODataBatchReaderState.ChangesetEnd;
                }
            }

            ODataBatchReaderState nextState;
            if (isEndBoundary || isParentBoundary)
            {
                // We detected an end boundary or detected that the end boundary is missing
                // because we found a parent boundary
                nextState = this.GetEndBoundaryState();

                if (nextState == ODataBatchReaderState.ChangesetEnd)
                {
                    // Reset the URL resolver at the end of a changeset; Content IDs are
                    // unique within a given changeset.
                    ResetPayloadUriConverter();
                }
            }
            else
            {
                bool currentlyInChangeSet = this.batchStream.ChangeSetBoundary != null;
                string contentId;

                // If we did not find an end boundary, we found another part
                bool isChangeSetPart = this.batchStream.ProcessPartHeader(out contentId);

                nextState = GetNextStateOnNewPart(currentlyInChangeSet, isChangeSetPart);

                if (!isChangeSetPart)
                {
                    VerifyAndSetContentId(contentId);
                }
            }

            return nextState;
        }
    }
}