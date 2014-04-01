//   OData .NET Libraries
//   Copyright (c) Microsoft Corporation
//   All rights reserved. 

//   Licensed under the Apache License, Version 2.0 (the ""License""); you may not use this file except in compliance with the License. You may obtain a copy of the License at http://www.apache.org/licenses/LICENSE-2.0 

//   THIS CODE IS PROVIDED ON AN  *AS IS* BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING WITHOUT LIMITATION ANY IMPLIED WARRANTIES OR CONDITIONS OF TITLE, FITNESS FOR A PARTICULAR PURPOSE, MERCHANTABLITY OR NON-INFRINGEMENT. 

//   See the Apache Version 2.0 License for specific language governing permissions and limitations under the License.

namespace Microsoft.OData.Core.Evaluation
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using Microsoft.OData.Edm;
    using Microsoft.OData.Core.JsonLight;
    using Microsoft.OData.Core.Metadata;

    /// <summary>
    /// Generates operations which were omitted by the service because they fully match conventions/templates and are always available.
    /// </summary>
    internal sealed class ODataMissingOperationGenerator
    {
        /// <summary>The current entry metadata context.</summary>
        private readonly IODataMetadataContext metadataContext;

        /// <summary>The metadata context of the entry to generate the missing operations for.</summary>
        private readonly IODataEntryMetadataContext entryMetadataContext;

        /// <summary>The list of computed actions.</summary>
        private List<ODataAction> computedActions;

        /// <summary>The list of computed functions.</summary>
        private List<ODataFunction> computedFunctions;

        /// <summary>
        /// Initializes a new instance of the <see cref="ODataMissingOperationGenerator"/> class.
        /// </summary>
        /// <param name="entryMetadataContext">The metadata context of the entry to generate the missing operations for.</param>
        /// <param name="metadataContext">The current entry metadata context.</param>
        internal ODataMissingOperationGenerator(IODataEntryMetadataContext entryMetadataContext, IODataMetadataContext metadataContext)
        {
            DebugUtils.CheckNoExternalCallers();
            Debug.Assert(entryMetadataContext != null, "entryMetadataCotext != null");
            Debug.Assert(metadataContext != null, "metadataContext != null");

            this.entryMetadataContext = entryMetadataContext;
            this.metadataContext = metadataContext;
        }

        /// <summary>
        /// Gets the computed missing Actions from the generator.
        /// </summary>
        /// <returns>The computed missing Actions.</returns>
        internal IEnumerable<ODataAction> GetComputedActions()
        {
            DebugUtils.CheckNoExternalCallers();
            this.ComputeMissingOperationsToEntry();
            return this.computedActions;
        }

        /// <summary>
        /// Gets the computed missing Functions from the generator.
        /// </summary>
        /// <returns>The computed missing Functions.</returns>
        internal IEnumerable<ODataFunction> GetComputedFunctions()
        {
            DebugUtils.CheckNoExternalCallers();
            this.ComputeMissingOperationsToEntry();
            return this.computedFunctions;
        }

        /// <summary>
        /// Returns a hash set of operation imports (actions and functions) in the given entry.
        /// </summary>
        /// <param name="entry">The entry in question.</param>
        /// <param name="model">The edm model to resolve operation imports.</param>
        /// <param name="metadataDocumentUri">The metadata document uri.</param>
        /// <returns>The hash set of operation imports (actions and functions) in the given entry.</returns>
        private static HashSet<IEdmOperationImport> GetOperationImportsInEntry(ODataEntry entry, IEdmModel model, Uri metadataDocumentUri)
        {
            Debug.Assert(entry != null, "entry != null");
            Debug.Assert(model != null, "model != null");
            Debug.Assert(metadataDocumentUri != null && metadataDocumentUri.IsAbsoluteUri, "metadataDocumentUri != null && metadataDocumentUri.IsAbsoluteUri");

            HashSet<IEdmOperationImport> operationImportsInEntry = new HashSet<IEdmOperationImport>(EqualityComparer<IEdmOperationImport>.Default);
            IEnumerable<ODataOperation> operations = ODataUtilsInternal.ConcatEnumerables((IEnumerable<ODataOperation>)entry.NonComputedActions, (IEnumerable<ODataOperation>)entry.NonComputedFunctions);
            if (operations != null)
            {
                foreach (ODataOperation operation in operations)
                {
                    Debug.Assert(operation.Metadata != null, "operation.Metadata != null");
                    string operationMetadataString = UriUtilsCommon.UriToString(operation.Metadata);
                    Debug.Assert(
                        ODataJsonLightUtils.IsMetadataReferenceProperty(operationMetadataString),
                        "ODataJsonLightUtils.IsMetadataReferenceProperty(operationMetadataString)");
                    Debug.Assert(
                        operationMetadataString[0] == JsonLightConstants.ContextUriFragmentIndicator || metadataDocumentUri.IsBaseOf(operation.Metadata),
                        "operationMetadataString[0] == JsonLightConstants.ContextUriFragmentIndicator || metadataDocumentUri.IsBaseOf(operation.Metadata)");

                    string fullyQualifiedOperationName = ODataJsonLightUtils.GetUriFragmentFromMetadataReferencePropertyName(metadataDocumentUri, operationMetadataString);
                    IEnumerable<IEdmOperationImport> operationImports = model.ResolveFunctionImports(fullyQualifiedOperationName);
                    if (operationImports != null)
                    {
                        foreach (IEdmOperationImport operationImport in operationImports)
                        {
                            operationImportsInEntry.Add(operationImport);
                        }
                    }
                }
            }

            return operationImportsInEntry;
        }

        /// <summary>
        /// Computes the operations that are missing from the payload but should be added by conventions onto the entry.
        /// </summary>
        private void ComputeMissingOperationsToEntry()
        {
            DebugUtils.CheckNoExternalCallers();
            Debug.Assert(this.entryMetadataContext != null, "this.entryMetadataContext != null");
            Debug.Assert(this.metadataContext != null, "this.metadataContext != null");

            if (this.computedActions == null)
            {
                Debug.Assert(this.computedFunctions == null, "this.computedFunctions == null");

                this.computedActions = new List<ODataAction>();
                this.computedFunctions = new List<ODataFunction>();
                HashSet<IEdmOperationImport> operationsInEntry = GetOperationImportsInEntry(this.entryMetadataContext.Entry, this.metadataContext.Model, this.metadataContext.MetadataDocumentUri);
                foreach (IEdmOperationImport alwaysBindableOperation in this.entryMetadataContext.SelectedAlwaysBindableOperations)
                {
                    // if this operation appears in the payload, skip it.
                    if (operationsInEntry.Contains(alwaysBindableOperation))
                    {
                        continue;
                    }

                    string metadataReferencePropertyName = JsonLightConstants.ContextUriFragmentIndicator + ODataJsonLightUtils.GetMetadataReferenceName(alwaysBindableOperation);
                    bool isAction;
                    ODataOperation operation = ODataJsonLightUtils.CreateODataOperation(this.metadataContext.MetadataDocumentUri, metadataReferencePropertyName, alwaysBindableOperation, out isAction);
                    operation.SetMetadataBuilder(this.entryMetadataContext.Entry.MetadataBuilder, this.metadataContext.MetadataDocumentUri);
                    if (isAction)
                    {
                        this.computedActions.Add((ODataAction)operation);
                    }
                    else
                    {
                        this.computedFunctions.Add((ODataFunction)operation);
                    }
                }
            }
        }
    }
}