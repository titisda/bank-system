﻿namespace CentralApi.Services.Implementations
{
    using Data;

    public abstract class BaseService
    {
        protected readonly CentralApiDbContext Context;

        protected BaseService(CentralApiDbContext context)
        {
            this.Context = context;
        }
    }
}