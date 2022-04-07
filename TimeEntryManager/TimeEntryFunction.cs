using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace TimeEntryManager
{
    public class TimeEntryFunction
    {
        private readonly ILogger<TimeEntryFunction> _logger;
        private ExecutionContext _context;
        private ServiceClient _service;

        #region constants
        public static readonly string DataverseTableName = "kk_timeentry";
        public static readonly string DataverseIdFieldName = "kk_timeentryid";
        public static readonly string DataversePrimaryFieldName = "kk_title";
        public static readonly string DataverseStartFieldName = "kk_start";
        public static readonly string DataverseEndFieldName = "kk_end";

        public static readonly string DataverseConnectionEnvKey = "MyDataverseConnection";
        public static readonly string DataverseDefaultConnection = "AuthType=OAuth;Username = KamranKarami@kk365env.onmicrosoft.com;Password = SimplePass123;Url = https://orgb66fe12c.crm11.dynamics.com;";

        public static readonly string PayloadJsonSchema = @"{
                '$schema': 'http://json-schema.org/draft-04/schema#',
                'type': 'object',
                'properties': {
                    'StartOn': {
                        'type': 'string',
                        'format': 'date'
                    },
                    'EndOn': {
                        'type': 'string',
                        'format': 'date'
                    }
                },
                'required': [
                    'StartOn',
                    'EndOn'
                  ]
             }";
        #endregion

        public TimeEntryFunction(ILogger<TimeEntryFunction> log)
        {
            _logger = log;
        }


        /// <summary>
        /// Azure function that gets a date range and insert all dates in range into dataverse
        /// </summary>
        /// <param name="req">payload json that contains StartOn and EndOn based on the given Schema</param>
        ///  <param name="context">payload json that contains StartOn and EndOn based on the given Schema</param>
        /// <returns>returns an ObjectResult with standard http response codes</returns>
        
        [FunctionName("TimeEntryFunction")]
        public async Task<IActionResult> Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req,
            ExecutionContext context)
        {
            _context = context;

            var requestBody = await new StreamReader(req.Body).ReadToEndAsync();
            var payload = GetPayload(requestBody, out var isPayloadValid);
            if (!isPayloadValid)
                return new BadRequestObjectResult("payload is not valid");

            var resMessage = PerformOperation(payload, out var isSucceed);
            return isSucceed ? new OkObjectResult(resMessage) : new StatusCodeResult(503); 
        }


        /// <summary>
        /// Converts the json string of input payload to TimeEntryFunctionPayload instance
        /// </summary>
        /// <param name="strPayload">json string of input payload</param>
        /// <param name="isValid">output boolean value that indicates whether the given payload has been valid or not</param>
        /// <returns>returns parsed object, which is an instance of a user-defined view model (TimeEntryFunctionPayload class)</returns>
        private TimeEntryFunctionPayload GetPayload(string strPayload, out bool isValid)
        {
            var schema = JSchema.Parse(PayloadJsonSchema);
            JObject parsedObject;
            try { parsedObject = JObject.Parse(strPayload); } 
            catch ( Exception ex )
            {
                _logger?.LogError(ex,"an error occurred in parsing the payload");
                isValid = false;
                return null;
            }
            isValid = parsedObject.IsValid(schema);
            if (!isValid)
                return null;

            var payload = new TimeEntryFunctionPayload( 
                    DateOnly.Parse((string)parsedObject.GetValue("StartOn") ?? string.Empty),
                    DateOnly.Parse((string)parsedObject.GetValue("EndOn") ?? string.Empty)
                );

            if (payload.StartOn > payload.EndOn)
                isValid = false;

            return payload;
        }


        /// <summary>
        /// perform the actual business rules of the task, inserting the dates in the given period
        /// </summary>
        /// <param name="payload">instance of TimeEntryFunctionPayload containing the period start and end</param>
        /// <param name="isSucceed">an output boolean value indicates the operation has been successful or not</param>
        /// <returns>returns an additional message in case of need to inform user</returns>
        private string PerformOperation(TimeEntryFunctionPayload payload, out bool isSucceed)
        {
            _service = new ServiceClient(GetConnectionString());

            CreateTimeEntryEntityIfNoteExists();
            
            if (_service is { IsReady: true })
            {
                var ids = new List<string>();
                var dateCursor = payload.StartOn;
                while (dateCursor <= payload.EndOn)
                {
                    if (!EntryExists(dateCursor))
                    {
                        var id = InsertEntry(dateCursor);
                        ids.Add(id.ToString());
                    }

                    dateCursor = dateCursor.AddDays(1);
                }
                isSucceed = true;
                return $"{ids.Count} time entries inserted.";
            }

            _logger?.LogError(_service.LastException, _service.LastError);
            Console.WriteLine($"Service is not available: {_service.LastError}");
            isSucceed = false;
            return null;

        }

        /// <summary>
        /// check if TimeEntry table exists in dataverse, and create the table if it does not exist
        /// </summary>
        private void CreateTimeEntryEntityIfNoteExists()
        {
            var tableExists = true;
            try
            { 
                _service.Execute(new RetrieveEntityRequest { EntityFilters = EntityFilters.Entity, LogicalName = DataverseTableName });
            }
            catch
            {
                tableExists = false;
            }

            if (tableExists)
                return;

            var requester = new CreateEntityRequest
            {
                //Define the entity
                Entity = new EntityMetadata
                {
                    SchemaName = DataverseTableName,
                    DisplayName = new Label("Time Entry", 1033),
                    DisplayCollectionName = new Label("Time Entries", 1033),
                    Description = new Label("An entity to store information about time entries", 1033),
                    OwnershipType = OwnershipTypes.UserOwned,
                    IsActivity = false
                },

                // Define the primary attribute for the entity
                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = DataversePrimaryFieldName,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.None),
                    MaxLength = 100,
                    FormatName = StringFormatName.Text,
                    DisplayName = new Label("Title", 1033),
                    Description = new Label("The primary attribute for the entity.", 1033)
                }
            };
            
            _service.Execute(requester);

            var createStartColRequest = new CreateAttributeRequest
            {
                EntityName = DataverseTableName,
                Attribute = new DateTimeAttributeMetadata
                {
                    SchemaName = DataverseStartFieldName,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                    Format = DateTimeFormat.DateOnly,
                    DisplayName = new Label("Start Date", 1033),
                    Description = new Label("The start date", 1033)
                }
            };

            _service.Execute(createStartColRequest);

            var createEndColRequest = new CreateAttributeRequest
            {
                EntityName = DataverseTableName,
                Attribute = new DateTimeAttributeMetadata
                {
                    SchemaName = DataverseEndFieldName,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                    Format = DateTimeFormat.DateOnly,
                    DisplayName = new Label("End Date", 1033),
                    Description = new Label("The end date", 1033)
                }
            };

            _service.Execute(createEndColRequest);

        }


        /// <summary>
        /// check whether the given date exists on the connected dataverse or not
        /// </summary>
        /// <param name="date"></param>
        /// <returns></returns>
        private bool EntryExists(DateOnly date)
        {
            var query = new QueryByAttribute(DataverseTableName)
            {
                TopCount = 1,
                ColumnSet = new ColumnSet(DataverseIdFieldName)
            };
            query.AddAttributeValue(DataverseStartFieldName, date.ToString());
            var results = _service.RetrieveMultiple(query);
            return results.Entities.Count > 0;
        }


        /// <summary>
        /// insert a new TimeEntry entity into the dataverse
        /// </summary>
        /// <param name="date">the date of the row to be used in start and end fields of target row</param>
        /// <returns>inserted id</returns>
        private Guid InsertEntry(DateOnly date)
        {
            var entry = new Entity(DataverseTableName)
            {
                 [DataversePrimaryFieldName] = date.ToString(),
                 [DataverseStartFieldName] = date.ToString(),
                 [DataverseEndFieldName] = date.ToString()
            };

            var newId = _service.Create(entry);
            return newId;
        }


        /// <summary>
        /// Gets connection string to the dataverse, from the DataverseConnectionEnvKey environment variable, and then local.host.settings and then from code
        /// </summary>
        /// <returns>returns the string that can be used to init Dataverse ServiceClient</returns>
        private string GetConnectionString()
        {
            try
            {
                //try reading from environment variables
                var conEnv = Environment.GetEnvironmentVariable(DataverseConnectionEnvKey);
                if (!string.IsNullOrEmpty(conEnv))
                    return conEnv;

                //try reading from local config, if not read from default constant(hard coded)
                var conConfig = GetConfig()?[DataverseConnectionEnvKey];
                return !string.IsNullOrEmpty(conConfig) ? conConfig : DataverseDefaultConnection;

            }
            catch (Exception)
            {
                //read from default (hard coded)
                return DataverseDefaultConnection;
            }
        }


        /// <summary>
        /// reads config settings from local.settings.json
        /// </summary>
        /// <returns></returns>
        private IConfigurationRoot GetConfig()
        {
            try
            {
                return new ConfigurationBuilder()
                    .SetBasePath(_context?.FunctionAppDirectory)
                    .AddJsonFile("local.settings.json", true, true)
                    .AddEnvironmentVariables()
                    .Build();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"Unable to load local.settings.json. exception: {ex}");
                return null;
            }
        }
    }
}

