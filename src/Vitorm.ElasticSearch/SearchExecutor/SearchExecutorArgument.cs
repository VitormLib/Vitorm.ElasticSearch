﻿using System.Collections.Generic;

using Vitorm.StreamQuery;

namespace Vitorm.ElasticSearch.QueryExecutor
{
    public class SearchExecutorArgument<ResultEntity>
    {
        public CombinedStream combinedStream;
        public Vitorm.ElasticSearch.DbContext dbContext;
        public string indexName;

        public bool needList;
        public bool needTotalCount;


        public IEnumerable<ResultEntity> list;
        public int? totalCount;
        public object extraResult;
    }

}