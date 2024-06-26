﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

using Vit.Extensions;

namespace Vitorm.ElasticSearch
{
    public partial class DbContext
    {

        #region #1.1 Schema :  Create

        public virtual async Task CreateAsync<Entity>()
        {
            var indexName = GetIndex<Entity>();
            await CreateAsync(indexName);
        }
        public virtual async Task<string> CreateAsync(string indexName, bool throwErrorIfFailed = false)
        {
            var url = $"{serverAddress}/{indexName}";
            var strPayload = "{\"mappings\":{\"properties\":{\"@timestamp\":{\"type\":\"date\"},\"time\":{\"type\":\"date\"}}}}";
            var content = new StringContent(strPayload, Encoding.UTF8, "application/json");

            var httpResponse = await httpClient.PutAsync(url, content);
            var strResponse = await httpResponse.Content.ReadAsStringAsync();

            if (throwErrorIfFailed && !httpResponse.IsSuccessStatusCode) throw new Exception(strResponse);
            return strResponse;
        }
        #endregion

        #region #1.2 Schema :  Drop
        public virtual async Task DropAsync<Entity>()
        {
            var indexName = GetIndex<Entity>();
            await DropAsync(indexName);
        }

        public virtual async Task DropAsync(string indexName)
        {
            var url = $"{serverAddress}/{indexName}";
            var httpResponse = await httpClient.DeleteAsync(url);

            if (httpResponse.IsSuccessStatusCode) return;

            var strResponse = await httpResponse.Content.ReadAsStringAsync();
            if (httpResponse.StatusCode == HttpStatusCode.NotFound && !string.IsNullOrWhiteSpace(strResponse)) return;

            throw new Exception(strResponse);
        }
        #endregion


        #region #1.1 Create :  Add

        public virtual async Task<Entity> AddAsync<Entity>(Entity entity)
        {
            var indexName = GetIndex<Entity>();
            return await AddAsync(entity, indexName);
        }
        public virtual async Task<Entity> AddAsync<Entity>(Entity entity, string indexName)
        {
            var entityDescriptor = GetEntityDescriptor(typeof(Entity));

            var _id = entityDescriptor.key.GetValue(entity) as string;
            var action = string.IsNullOrWhiteSpace(_id) ? "_doc" : "_create";

            return await SingleActionAsync(entityDescriptor, entity, indexName, action);
        }

        #endregion


        #region #1.2 Create :  AddRange
        public virtual async Task AddRangeAsync<Entity>(IEnumerable<Entity> entities)
        {
            var indexName = GetIndex<Entity>();
            await AddRangeAsync(entities, indexName);
        }
        public virtual async Task AddRangeAsync<Entity>(IEnumerable<Entity> entities, string indexName)
        {
            var entityDescriptor = GetEntityDescriptor(typeof(Entity));
            var bulkResult = await BulkAsync(entityDescriptor, entities, indexName, "create");

            if (bulkResult.errors == true)
            {
                var reason = bulkResult.items?.FirstOrDefault(m => m.result?.error?.reason != null)?.result?.error?.reason;
                ThrowException(reason, bulkResult.responseBody);
            }

            var items = bulkResult?.items;
            if (items?.Length == entities.Count())
            {
                var t = 0;
                foreach (var entity in entities)
                {
                    var id = items[t].result?._id;
                    if (id != null) entityDescriptor.key?.SetValue(entity, id);
                    t++;
                }
            }
        }
        #endregion




        #region #2.1 Retrieve : Get

        public virtual async Task<Entity> GetAsync<Entity>(object keyValue)
        {
            var indexName = GetIndex<Entity>();
            return await GetAsync<Entity>(keyValue, indexName);
        }
        public virtual async Task<Entity> GetAsync<Entity>(object keyValue, string indexName)
        {
            var actionUrl = $"{serverAddress}/{indexName}/_doc/" + keyValue;

            var httpResponse = await httpClient.GetAsync(actionUrl);

            if (httpResponse.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            httpResponse.EnsureSuccessStatusCode();

            var strResponse = await httpResponse.Content.ReadAsStringAsync();
            var response = Deserialize<GetResult<Entity>>(strResponse);

            if (response.found != true) return default;

            var entity = response._source;
            if (entity != null && response._id != null)
            {
                var entityDescriptor = GetEntityDescriptor(typeof(Entity));
                entityDescriptor.key.SetValue(entity, response._id);
            }
            return entity;
        }

        /// <summary>
        /// result for   GET dev-orm/_doc/3
        /// </summary>
        /// <typeparam name="Entity"></typeparam>
        class GetResult<Entity>
        {
            public string _index { get; set; }
            public string _id { get; set; }

            public string _type { get; set; }
            public int? _version { get; set; }

            public int? _seq_no { get; set; }
            public int? _primary_term { get; set; }
            public bool? found { get; set; }
            public Entity _source { get; set; }
        }
        #endregion


        #region #3 Update: Update UpdateRange
        public virtual async Task<int> UpdateAsync<Entity>(Entity entity)
        {
            return await UpdateRangeAsync<Entity>(new[] { entity });
        }

        public virtual async Task<int> UpdateAsync<Entity>(Entity entity, string indexName)
        {
            return await UpdateRangeAsync<Entity>(new[] { entity }, indexName);
        }


        public virtual async Task<int> UpdateRangeAsync<Entity>(IEnumerable<Entity> entities)
        {
            var indexName = GetIndex<Entity>();
            return await UpdateRangeAsync<Entity>(entities, indexName);
        }

        public virtual async Task<int> UpdateRangeAsync<Entity>(IEnumerable<Entity> entities, string indexName)
        {
            var key = GetEntityDescriptor(typeof(Entity)).key;
            if (entities.Any(entity => string.IsNullOrWhiteSpace(key.GetValue(entity) as string))) throw new ArgumentNullException("_id");

            var entityDescriptor = GetEntityDescriptor(typeof(Entity));
            var bulkResult = await BulkAsync(entityDescriptor, entities, indexName, "update");

            if (bulkResult.items.Any() != true) ThrowException(bulkResult.responseBody);

            var rowCount = bulkResult.items.Count(item => item.update?.status == 200);

            return rowCount;
        }

        #endregion



        #region Save SaveRange
        public virtual async Task<int> SaveAsync<Entity>(Entity entity)
        {
            var indexName = GetIndex<Entity>();
            return await SaveAsync<Entity>(entity, indexName);
        }

        public virtual async Task<int> SaveAsync<Entity>(Entity entity, string indexName)
        {
            var entityDescriptor = GetEntityDescriptor(typeof(Entity));
            entity = await SingleActionAsync(entityDescriptor, entity, indexName, "_doc");
            return entity != null ? 1 : 0;
        }

        public virtual async Task SaveRangeAsync<Entity>(IEnumerable<Entity> entities)
        {
            var indexName = GetIndex<Entity>();
            await SaveRangeAsync<Entity>(entities, indexName);
        }

        public virtual async Task SaveRangeAsync<Entity>(IEnumerable<Entity> entities, string indexName)
        {
            var entityDescriptor = GetEntityDescriptor(typeof(Entity));
            var bulkResult = await BulkAsync(entityDescriptor, entities, indexName, "index");

            if (bulkResult.errors == true)
            {
                var reason = bulkResult.items?.FirstOrDefault(m => m.result?.error?.reason != null)?.result?.error?.reason;
                ThrowException(reason, bulkResult.responseBody);
            }

            var items = bulkResult?.items;
            if (items?.Length == entities.Count())
            {
                var key = entityDescriptor.key;
                var t = 0;
                foreach (var entity in entities)
                {
                    var id = items[t].result?._id;
                    if (id != null) key.SetValue(entity, id);
                    t++;
                }
            }
        }
        #endregion



        #region #4 Delete : Delete DeleteRange DeleteByKey DeleteByKeys

        public virtual async Task<int> DeleteAsync<Entity>(Entity entity)
        {
            var indexName = GetIndex<Entity>();
            return await DeleteAsync<Entity>(entity, indexName);
        }
        public virtual async Task<int> DeleteAsync<Entity>(Entity entity, string indexName)
        {
            var entityDescriptor = GetEntityDescriptor(typeof(Entity));

            var key = entityDescriptor.key.GetValue(entity);
            return await DeleteByKeyAsync(key, indexName);
        }



        public virtual async Task<int> DeleteRangeAsync<Entity>(IEnumerable<Entity> entities)
        {
            var entityDescriptor = GetEntityDescriptor(typeof(Entity));

            var keys = entities.Select(entity => entityDescriptor.key.GetValue(entity)).ToList();
            return await DeleteByKeysAsync<Entity, object>(keys);
        }
        public virtual async Task<int> DeleteRangeAsync<Entity>(IEnumerable<Entity> entities, string indexName)
        {
            var entityDescriptor = GetEntityDescriptor(typeof(Entity));

            var keys = entities.Select(entity => entityDescriptor.key.GetValue(entity)).ToList();
            return await DeleteByKeysAsync<Entity, object>(keys, indexName);
        }


        public virtual async Task<int> DeleteByKeyAsync<Entity>(object keyValue)
        {
            var indexName = GetIndex<Entity>();
            return await DeleteByKeyAsync(keyValue, indexName);
        }
        public virtual async Task<int> DeleteByKeyAsync(object keyValue, string indexName)
        {
            var _id = keyValue?.ToString();

            if (string.IsNullOrWhiteSpace(_id)) throw new ArgumentNullException("_id");

            var actionUrl = $"{serverAddress}/{indexName}/_doc/" + _id;

            var httpResponse = await httpClient.DeleteAsync(actionUrl);
            return httpResponse.IsSuccessStatusCode ? 1 : 0;

            //var strResponse = httpResponse.Content.ReadAsStringAsync().Result;
            /*
            {
              "_index": "user",
              "_type": "_doc",
              "_id": "5",
              "_version": 2,
              "result": "deleted",
              "_shards": {
                "total": 2,
                "successful": 1,
                "failed": 0
              },
              "_seq_no": 6,
              "_primary_term": 1
            }
            */
        }



        public virtual async Task<int> DeleteByKeysAsync<Entity, Key>(IEnumerable<Key> keys)
        {
            var indexName = GetIndex<Entity>();
            return await DeleteByKeysAsync<Entity, Key>(keys, indexName);
        }
        public virtual async Task<int> DeleteByKeysAsync<Entity, Key>(IEnumerable<Key> keys, string indexName)
        {
            var payload = new StringBuilder();
            foreach (var _id in keys)
            {
                payload.AppendLine($"{{\"delete\":{{\"_index\":\"{indexName}\",\"_id\":\"{_id}\"}}}}");
            }
            var actionUrl = $"{serverAddress}/{indexName}/_bulk";
            var content = new StringContent(payload.ToString(), Encoding.UTF8, "application/json");
            var httpResponse = await httpClient.PostAsync(actionUrl, content);

            var strResponse = await httpResponse.Content.ReadAsStringAsync();
            if (string.IsNullOrWhiteSpace(strResponse)) httpResponse.EnsureSuccessStatusCode();

            var response = Deserialize<BulkResponse>(strResponse);

            if (response.errors == true)
            {
                var reason = response.items?.FirstOrDefault(m => m.result?.error?.reason != null)?.result?.error?.reason;
                ThrowException(reason, strResponse);
            }

            return response.items.Count(item => item.result?.status == 200);
        }



        #endregion

    }
}
