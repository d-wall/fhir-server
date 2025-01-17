﻿// -------------------------------------------------------------------------------------------------
// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License (MIT). See LICENSE in the repo root for license information.
// -------------------------------------------------------------------------------------------------

using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Health.Extensions.DependencyInjection;
using Microsoft.Health.Fhir.Core.Extensions;
using Microsoft.Health.Fhir.Core.Features.Operations;
using Microsoft.Health.Fhir.Core.Features.Operations.Export;
using Microsoft.Health.Fhir.Core.Features.Operations.Export.Models;
using Microsoft.Health.Fhir.Core.Features.Persistence;
using Microsoft.Health.Fhir.Core.Messages.Export;
using NSubstitute;
using Xunit;

namespace Microsoft.Health.Fhir.Core.UnitTests.Features.Operations.Export
{
    public class GetExportRequestHandlerTests
    {
        private readonly IFhirOperationDataStore _fhirOperationDataStore = Substitute.For<IFhirOperationDataStore>();
        private readonly IMediator _mediator;

        private readonly Uri _createRequestUri = new Uri("https://localhost/$export/");
        private const string _failureReason = "ExportJobFailed";
        private HttpStatusCode _failureStatusCode = HttpStatusCode.InternalServerError;

        public GetExportRequestHandlerTests()
        {
            var collection = new ServiceCollection();
            collection.Add(x => new GetExportRequestHandler(_fhirOperationDataStore)).Singleton().AsSelf().AsImplementedInterfaces();

            ServiceProvider provider = collection.BuildServiceProvider();
            _mediator = new Mediator(type => provider.GetService(type));
        }

        [Fact]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithCompletedStatus_ThenHttpResponseCodeShouldBeOk()
        {
            GetExportResponse result = await SetupAndExecuteGetExportJobByIdAsync(OperationStatus.Completed);

            Assert.Equal(HttpStatusCode.OK, result.StatusCode);
            Assert.NotNull(result.JobResult);

            // Check whether required fields are present.
            Assert.NotNull(result.JobResult.Output);
            Assert.NotEqual(default, result.JobResult.TransactionTime);
            Assert.NotNull(result.JobResult.RequestUri);
            Assert.NotNull(result.JobResult.Error);
        }

        [Theory]
        [InlineData(OperationStatus.Canceled, HttpStatusCode.NoContent)]
        [InlineData(OperationStatus.Canceled, HttpStatusCode.InternalServerError)]
        [InlineData(OperationStatus.Failed, HttpStatusCode.BadRequest)]
        [InlineData(OperationStatus.Failed, HttpStatusCode.InternalServerError)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithFailedStatus_ThenOperationFailedExceptionIsThrownWithCorrectHttpResponseCode(OperationStatus operationStatus, HttpStatusCode failureStatusCode)
        {
            _failureStatusCode = failureStatusCode;

            OperationFailedException ofe = await Assert.ThrowsAsync<OperationFailedException>(() => SetupAndExecuteGetExportJobByIdAsync(operationStatus));

            Assert.NotNull(ofe);
            Assert.Equal(failureStatusCode, ofe.ResponseStatusCode);
            Assert.Contains(_failureReason, ofe.Message);
        }

        [Theory]
        [InlineData(OperationStatus.Running)]
        [InlineData(OperationStatus.Queued)]
        public async Task GivenAFhirMediator_WhenGettingAnExistingExportJobWithNotCompletedStatus_ThenHttpResponseCodeShouldBeAccepted(OperationStatus operationStatus)
        {
            GetExportResponse result = await SetupAndExecuteGetExportJobByIdAsync(operationStatus);

            Assert.Equal(HttpStatusCode.Accepted, result.StatusCode);
            Assert.Null(result.JobResult);
        }

        private async Task<GetExportResponse> SetupAndExecuteGetExportJobByIdAsync(OperationStatus jobStatus)
        {
            var jobRecord = new ExportJobRecord(_createRequestUri, "Patient", "hash")
            {
                Status = jobStatus,
            };

            if (jobStatus == OperationStatus.Canceled || jobStatus == OperationStatus.Failed)
            {
                jobRecord.FailureDetails = new ExportJobFailureDetails(_failureReason, _failureStatusCode);
            }

            var jobOutcome = new ExportJobOutcome(jobRecord, WeakETag.FromVersionId("eTag"));

            _fhirOperationDataStore.GetExportJobByIdAsync(jobRecord.Id, Arg.Any<CancellationToken>()).Returns(jobOutcome);

            return await _mediator.GetExportStatusAsync(_createRequestUri, jobRecord.Id, CancellationToken.None);
        }
    }
}
