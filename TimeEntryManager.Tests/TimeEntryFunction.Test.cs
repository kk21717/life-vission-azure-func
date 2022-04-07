using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;

namespace TimeEntryManager.Tests
{
    public class Tests
    {
        private readonly TimeEntryFunction _func = new(null); 
        private readonly ServiceClient _service = new(TimeEntryFunction.DataverseDefaultConnection);

        [SetUp]
        public void Setup()
        {
            CreateTimeEntryEntityIfNoteExists();
        }

        [Test]
        public void InvalidPayload_BadFormat()
        {
            const string payload = "Some not-json data!";
            var req = GetHttpRequestFor(payload);
            var res = (ObjectResult)_func.Run(req,null).Result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, res.StatusCode);
        }

        [Test]
        public void InvalidPayload_BadDataType()
        {
            dynamic payload = new { StartOn = "bad data type", EndOn = "2022-04-06 17:27:00.000" };
            var req = GetHttpRequestFor(payload);
            var res = (ObjectResult)_func.Run(req,null).Result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, res.StatusCode);
        }

        [Test]
        public void InvalidPayload_MissingParameter()
        {
            dynamic payload = new TimeEntryFunctionPayload
            {
                StartOn = new DateOnly(2022, 01, 03)
            };
            var req = GetHttpRequestFor(payload);
            var res = (ObjectResult)_func.Run(req,null).Result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, res.StatusCode);
        }

        [Test]
        public void InvalidPayload_StartOnIsLaterThanEndOn()
        {
            dynamic payload = new TimeEntryFunctionPayload
            {
                StartOn = new DateOnly(2022, 01, 03),
                EndOn = new DateOnly(2022,01,02)
            };
            var req = GetHttpRequestFor(payload);
            var res = (ObjectResult)_func.Run(req,null).Result;
            Assert.AreEqual(StatusCodes.Status400BadRequest, res.StatusCode);
        }

        [Test]
        public void PerformInsert_SingleDayPeriod()
        {
            #region prepare test inputs
            var startDate = new DateOnly(2022, 01, 03);
            var endDate = new DateOnly(2022, 01, 03);
            #endregion

            #region remove existing rows from dataverse
            var query = new QueryByAttribute(TimeEntryFunction.DataverseTableName){ TopCount = 50,ColumnSet = new ColumnSet(TimeEntryFunction.DataverseIdFieldName)};
            query.AddAttributeValue(TimeEntryFunction.DataverseStartFieldName, startDate.ToString());
            var resPreInsert = _service.RetrieveMultiple(query);
            if (resPreInsert.Entities.Count > 0)
            {
                foreach (var item in resPreInsert.Entities)
                {
                    _service.Delete(TimeEntryFunction.DataverseTableName, item.Id);
                }
            }
            #endregion

            #region perform operation
            var payload = new TimeEntryFunctionPayload
            {
                StartOn = startDate,
                EndOn = endDate
            };
            var req = GetHttpRequestFor(payload.ToJson());
            var res = (ObjectResult)_func.Run(req,null).Result;
            #endregion

            #region Assertions
            //assertion 1 (200 response)
            Assert.AreEqual(StatusCodes.Status200OK, res.StatusCode);

            //assertion 2 (date exists on dataverse) 
            var resPostInsert = _service.RetrieveMultiple(query);
            Assert.AreEqual(1, resPostInsert.Entities.Count);
            #endregion
        }

        [Test]
        public void PerformInsert_MultiDaysPeriod()
        {
            #region prepare test inputs
            var startDate = new DateOnly(2022, 01, 03);
            var endDate = new DateOnly(2022, 01, 05);
            var dates = new List<string>();
            var dateCursor = startDate;
            while (dateCursor <= endDate)
            {
                dates.Add(dateCursor.ToString());
                dateCursor = dateCursor.AddDays(1);
            }
            #endregion

            #region remove existing rows from dataverse
            var queryPreInsert = new QueryExpression(TimeEntryFunction.DataverseTableName)
            {
                ColumnSet = new ColumnSet(TimeEntryFunction.DataverseIdFieldName),
                Criteria = new FilterExpression(LogicalOperator.Or),
                TopCount = 50
            };
            dates.ForEach(date => queryPreInsert.Criteria.AddCondition(TimeEntryFunction.DataverseStartFieldName, ConditionOperator.Equal, date));
            var resPreInsert = _service.RetrieveMultiple(queryPreInsert);
            if (resPreInsert.Entities.Count > 0)
            {
                foreach (var item in resPreInsert.Entities)
                {
                    _service.Delete(TimeEntryFunction.DataverseTableName, item.Id);
                }
            }
            #endregion

            #region perform operation
            var payload = new TimeEntryFunctionPayload(startDate, endDate);
            var req = GetHttpRequestFor(payload.ToJson());
            var res = (ObjectResult)_func.Run(req,null).Result;
            #endregion

            #region Assertions
            //assertion 1 (200 response)
            Assert.AreEqual(StatusCodes.Status200OK, res.StatusCode);

            //assertion 2 (date exists on dataverse) 
            var queryPostInsert = new QueryExpression(TimeEntryFunction.DataverseTableName)
            {
                ColumnSet = new ColumnSet(TimeEntryFunction.DataverseIdFieldName),
                Criteria = new FilterExpression(LogicalOperator.Or),
                TopCount = 50
            };
            dates.ForEach(date => queryPostInsert.Criteria.AddCondition(TimeEntryFunction.DataverseStartFieldName, ConditionOperator.Equal, date));
            var resPostInsert = _service.RetrieveMultiple(queryPostInsert);
            Assert.AreEqual(dates.Count, resPostInsert.Entities.Count);
            #endregion
        }

        [Test]
        public void PerformInsert_WithExistingDaysInPeriod()
        {
            #region prepare test inputs
            var startDate = new DateOnly(2022, 01, 03);
            var endDate = new DateOnly(2022, 01, 07);
            var dates = new List<string>();
            var dateCursor = startDate;
            while (dateCursor <= endDate)
            {
                dates.Add(dateCursor.ToString());
                dateCursor = dateCursor.AddDays(1);
            }
            var existingDate1 = new DateOnly(2022, 01, 05);
            var existingDate2 = new DateOnly(2022, 01, 07);
            #endregion

            #region remove existing rows from dataverse
            var queryPreInsert = new QueryExpression(TimeEntryFunction.DataverseTableName)
            {
                ColumnSet = new ColumnSet(TimeEntryFunction.DataverseIdFieldName),
                Criteria = new FilterExpression(LogicalOperator.Or),
                TopCount = 50
            };
            dates.ForEach(date => queryPreInsert.Criteria.AddCondition(TimeEntryFunction.DataverseStartFieldName, ConditionOperator.Equal, date));
            var resPreInsert = _service.RetrieveMultiple(queryPreInsert);
            if (resPreInsert.Entities.Count > 0)
            {
                foreach (var item in resPreInsert.Entities)
                {
                    _service.Delete(TimeEntryFunction.DataverseTableName, item.Id);
                }
            }
            #endregion

            #region add temporary days in (in the period range) to dataverse
            var entry1 = new Entity(TimeEntryFunction.DataverseTableName)
            {
                 [TimeEntryFunction.DataversePrimaryFieldName] = existingDate1.ToString(),
                 [TimeEntryFunction.DataverseStartFieldName] = existingDate1.ToString(),
                 [TimeEntryFunction.DataverseEndFieldName] = existingDate1.ToString()
            };

            var entry2 = new Entity(TimeEntryFunction.DataverseTableName)
            {
                 [TimeEntryFunction.DataversePrimaryFieldName] = existingDate2.ToString(),
                 [TimeEntryFunction.DataverseStartFieldName] = existingDate2.ToString(),
                 [TimeEntryFunction.DataverseEndFieldName] = existingDate2.ToString()
            };

            _service.Create(entry1);
            _service.Create(entry2);
            #endregion

            #region perform operation
            var payload = new TimeEntryFunctionPayload(startDate, endDate);
            var req = GetHttpRequestFor(payload.ToJson());
            var res = (ObjectResult)_func.Run(req,null).Result;
            #endregion

            #region Assertions
            //assertion condition 1 (200 response)
            Assert.AreEqual(StatusCodes.Status200OK, res.StatusCode);

            //assertion condition 2 (date exists on dataverse) 
            var queryPostInsert = new QueryExpression(TimeEntryFunction.DataverseTableName)
            {
                ColumnSet = new ColumnSet(TimeEntryFunction.DataverseIdFieldName),
                Criteria = new FilterExpression(LogicalOperator.Or),
                TopCount = 50
            };
            dates.ForEach(date => queryPostInsert.Criteria.AddCondition(TimeEntryFunction.DataverseStartFieldName, ConditionOperator.Equal, date));
            var resPostInsert = _service.RetrieveMultiple(queryPostInsert);
            Assert.AreEqual(dates.Count, resPostInsert.Entities.Count);
            #endregion
        }

        private static HttpRequest GetHttpRequestFor(object data)
        {
            var jObject = JObject.FromObject(data);
            return GetHttpRequestFor(jObject.ToString());
        }
        private static HttpRequest GetHttpRequestFor(string data)
        {
            var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(data));
            HttpContext httpContext = new DefaultHttpContext();
            httpContext.Request.Body = stream;
            httpContext.Request.ContentLength = stream.Length;
            httpContext.Request.Method = "POST";
            return httpContext.Request;
        }

        /// <summary>
        /// check if TimeEntry table exists in dataverse, and create the table if it does not exist
        /// </summary>
        private void CreateTimeEntryEntityIfNoteExists()
        {
            var tableExists = true;
            try
            {
                _service.Execute(new RetrieveEntityRequest { EntityFilters = EntityFilters.Entity, LogicalName = TimeEntryFunction.DataverseTableName });
            }
            catch
            {
                tableExists = false;
            }

            if (tableExists)
                return;

            var requester = new CreateEntityRequest
            {
                Entity = new EntityMetadata
                {
                    SchemaName = TimeEntryFunction.DataverseTableName,
                    DisplayName = new Label("Time Entry", 1033),
                    DisplayCollectionName = new Label("Time Entries", 1033),
                    Description = new Label("An entity to store information about time entries", 1033),
                    OwnershipType = OwnershipTypes.UserOwned,
                    IsActivity = false
                },

                PrimaryAttribute = new StringAttributeMetadata
                {
                    SchemaName = TimeEntryFunction.DataversePrimaryFieldName,
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
                EntityName = TimeEntryFunction.DataverseTableName,
                Attribute = new DateTimeAttributeMetadata
                {
                    SchemaName = TimeEntryFunction.DataverseStartFieldName,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                    Format = DateTimeFormat.DateOnly,
                    DisplayName = new Label("Start Date", 1033),
                    Description = new Label("The start date", 1033)
                }
            };

            _service.Execute(createStartColRequest);

            var createEndColRequest = new CreateAttributeRequest
            {
                EntityName = TimeEntryFunction.DataverseTableName,
                Attribute = new DateTimeAttributeMetadata
                {
                    SchemaName = TimeEntryFunction.DataverseEndFieldName,
                    RequiredLevel = new AttributeRequiredLevelManagedProperty(AttributeRequiredLevel.ApplicationRequired),
                    Format = DateTimeFormat.DateOnly,
                    DisplayName = new Label("End Date", 1033),
                    Description = new Label("The end date", 1033)
                }
            };

            _service.Execute(createEndColRequest);

        }

    }
}