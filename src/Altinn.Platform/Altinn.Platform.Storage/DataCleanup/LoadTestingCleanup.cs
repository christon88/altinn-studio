using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Altinn.Platform.Storage.DataCleanup.Services;
using Altinn.Platform.Storage.Interface.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;

namespace Altinn.Platform.Storage.DataCleanup
{
    /// <summary>
    /// Azure Function class for handling tasks related to load testing cleanup
    /// </summary>
    public class LoadTestingCleanup
    {
        private readonly ICosmosService _cosmosService;
        private readonly IBlobService _blobService;

        /// <summary>
        /// Initializes a new instance of the <see cref="LoadTestingCleanup"/> class.
        /// </summary>
        /// <param name="cosmosService">The Cosmos DB service.</param>
        /// <param name="blobService">The blob service.</param>
        public LoadTestingCleanup(ICosmosService cosmosService, IBlobService blobService)
        {
            _cosmosService = cosmosService;
            _blobService = blobService;
        }

        /// <summary>
        /// Runs load testing cleanup.
        /// </summary>
        /// <param name="req">The http request.</param>
        /// <param name="log">The log.</param>
        [FunctionName("LoadTestingCleanup")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            string app = req.Query["app"];

            if (string.IsNullOrEmpty(app))
            {
                return new BadRequestObjectResult("Pass an app name in the query string to clean up load testing data.");
            }

            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
            List<Instance> instances = await _cosmosService.GetAllInstancesOfApp(app.ToLower().Trim());

            int successfullyDeleted = 0;

            foreach (Instance instance in instances)
            {
                bool dataElementsDeleted, dataElementMetadataDeleted = false;

                try
                {
                    dataElementsDeleted = await _blobService.DeleteDataBlobs(instance);

                    if (dataElementsDeleted)
                    {
                        dataElementMetadataDeleted = await _cosmosService.DeleteDataElementDocuments(instance.Id);
                    }

                    bool instanceEventsDeleted = await _cosmosService.DeleteInstanceEventDocuments(instance.Id, instance.InstanceOwner.PartyId);

                    if (dataElementMetadataDeleted && instanceEventsDeleted)
                    {
                        await _cosmosService.DeleteInstanceDocument(instance.Id, instance.InstanceOwner.PartyId);
                        successfullyDeleted += 1;
                        log.LogInformation(
                            "LoadTestingCleanup // Run // Instance deleted: {AppId}/{InstanceId}",
                            instance.AppId,
                            $"{instance.InstanceOwner.PartyId}/{instance.Id}");
                    }
                }
                catch (Exception e)
                {
                    log.LogError(
                        "LoadTestingCleanup // Run // Error occured when deleting instance: {AppId}/{InstanceId} \r Exception {Exception}",
                        instance.AppId,
                        $"{instance.InstanceOwner.PartyId}/{instance.Id}",
                        e);
                }
            }

            stopwatch.Stop();

            log.LogInformation(
                "LoadTestingCleanup // Run // {DeleteCount} of {OriginalCount} instances deleted in {Duration} s",
                successfullyDeleted,
                instances.Count,
                stopwatch.Elapsed.TotalSeconds);

            return new OkObjectResult($"{successfullyDeleted}/{instances.Count} instances and all related data has been successfully deleted.");
        }
    }
}
