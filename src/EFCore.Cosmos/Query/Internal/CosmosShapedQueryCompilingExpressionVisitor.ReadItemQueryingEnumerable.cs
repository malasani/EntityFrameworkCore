﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.ChangeTracking.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.Internal;
using Microsoft.EntityFrameworkCore.Cosmos.Storage.Internal;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Internal;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Conventions;
using Microsoft.EntityFrameworkCore.Query;
using Newtonsoft.Json.Linq;

namespace Microsoft.EntityFrameworkCore.Cosmos.Query.Internal
{
    /// <summary>
    ///     This is an internal API that supports the Entity Framework Core infrastructure and not subject to
    ///     the same compatibility standards as public APIs. It may be changed or removed without notice in
    ///     any release. You should only use it directly in your code with extreme caution and knowing that
    ///     doing so can result in application failures when updating to a new Entity Framework Core release.
    /// </summary>
    public partial class CosmosShapedQueryCompilingExpressionVisitor
    {
        private sealed class ReadItemQueryingEnumerable<T> : IEnumerable<T>, IAsyncEnumerable<T>, IQueryingEnumerable
        {
            private readonly CosmosQueryContext _cosmosQueryContext;
            private readonly ReadItemExpression _readItemExpression;
            private readonly Func<CosmosQueryContext, JObject, T> _shaper;
            private readonly Type _contextType;
            private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;
            private readonly bool _performIdentityResolution;

            public ReadItemQueryingEnumerable(
                CosmosQueryContext cosmosQueryContext,
                ReadItemExpression readItemExpression,
                Func<CosmosQueryContext, JObject, T> shaper,
                Type contextType,
                IDiagnosticsLogger<DbLoggerCategory.Query> logger,
                bool performIdentityResolution)
            {
                _cosmosQueryContext = cosmosQueryContext;
                _readItemExpression = readItemExpression;
                _shaper = shaper;
                _contextType = contextType;
                _logger = logger;
                _performIdentityResolution = performIdentityResolution;
            }

            public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
                => new Enumerator(this, cancellationToken);

            public IEnumerator<T> GetEnumerator() => new Enumerator(this);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            public string ToQueryString()
            {
                throw new NotImplementedException("Cosmos: ToQueryString for ReadItemQueryingEnumerable #20653");
            }

            private sealed class Enumerator : IEnumerator<T>, IAsyncEnumerator<T>
            {
                private readonly CosmosQueryContext _cosmosQueryContext;
                private readonly ReadItemExpression _readItemExpression;
                private readonly Func<CosmosQueryContext, JObject, T> _shaper;
                private readonly Type _contextType;
                private readonly IDiagnosticsLogger<DbLoggerCategory.Query> _logger;
                private readonly bool _performIdentityResolution;
                private readonly CancellationToken _cancellationToken;

                private JObject _item;
                private bool _hasExecuted;

                public Enumerator(ReadItemQueryingEnumerable<T> readItemEnumerable, CancellationToken cancellationToken = default)
                {
                    _cosmosQueryContext = readItemEnumerable._cosmosQueryContext;
                    _readItemExpression = readItemEnumerable._readItemExpression;
                    _shaper = readItemEnumerable._shaper;
                    _contextType = readItemEnumerable._contextType;
                    _logger = readItemEnumerable._logger;
                    _performIdentityResolution = readItemEnumerable._performIdentityResolution;
                    _cancellationToken = cancellationToken;
                }

                object IEnumerator.Current => Current;

                public T Current { get; private set; }

                public bool MoveNext()
                {
                    try
                    {
                        using (_cosmosQueryContext.ConcurrencyDetector.EnterCriticalSection())
                        {
                            if (!_hasExecuted)
                            {
                                if (!TryGetResourceId(out var resourceId))
                                {
                                    throw new InvalidOperationException(CosmosStrings.ResourceIdMissing);
                                }

                                if (!TryGetPartitionId(out var partitionKey))
                                {
                                    throw new InvalidOperationException(CosmosStrings.ParitionKeyMissing);
                                }

                                _item = _cosmosQueryContext.CosmosClient.ExecuteReadItem(
                                    _readItemExpression.Container,
                                    partitionKey,
                                    resourceId);

                                return ShapeResult();
                            }

                            return false;
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.QueryIterationFailed(_contextType, exception);

                        throw;
                    }
                }

                public async ValueTask<bool> MoveNextAsync()
                {
                    try
                    {
                        using (_cosmosQueryContext.ConcurrencyDetector.EnterCriticalSection())
                        {
                            if (!_hasExecuted)
                            {

                                if (!TryGetResourceId(out var resourceId))
                                {
                                    throw new InvalidOperationException(CosmosStrings.ResourceIdMissing);
                                }

                                if (!TryGetPartitionId(out var partitionKey))
                                {
                                    throw new InvalidOperationException(CosmosStrings.ParitionKeyMissing);
                                }

                                _item = await _cosmosQueryContext.CosmosClient.ExecuteReadItemAsync(
                                    _readItemExpression.Container,
                                    partitionKey,
                                    resourceId,
                                    _cancellationToken);

                                return ShapeResult();
                            }

                            return false;
                        }
                    }
                    catch (Exception exception)
                    {
                        _logger.QueryIterationFailed(_contextType, exception);

                        throw;
                    }
                }

                public void Dispose()
                {
                    _item = null;
                    _hasExecuted = false;
                }

                public ValueTask DisposeAsync()
                {
                    Dispose();

                    return default;
                }

                public void Reset() => throw new NotImplementedException();

                private bool ShapeResult()
                {
                    var hasNext = !(_item is null);

                    _cosmosQueryContext.InitializeStateManager(_performIdentityResolution);

                    Current
                        = hasNext
                            ? _shaper(_cosmosQueryContext, _item)
                            : default;

                    _hasExecuted = true;

                    return hasNext;
                }

                private bool TryGetPartitionId(out string partitionKey)
                {
                    partitionKey = null;

                    var partitionKeyPropertyName = _readItemExpression.EntityType.GetPartitionKeyPropertyName();
                    if (partitionKeyPropertyName == null)
                    {
                        return true;
                    }

                    var partitionKeyProperty = _readItemExpression.EntityType.FindProperty(partitionKeyPropertyName);

                    if (TryGetParameterValue(partitionKeyProperty, out var value))
                    {
                        partitionKey = GetString(partitionKeyProperty, value);

                        return !string.IsNullOrEmpty(partitionKey);
                    }

                    return false;
                }

                private bool TryGetResourceId(out string resourceId)
                {
                    var idProperty = _readItemExpression.EntityType.GetProperties()
                        .FirstOrDefault(p => p.GetJsonPropertyName() == StoreKeyConvention.IdPropertyName);

                    if (TryGetParameterValue(idProperty, out var value))
                    {
                        resourceId = GetString(idProperty, value);

                        if (string.IsNullOrEmpty(resourceId))
                        {
                            throw new InvalidOperationException(CosmosStrings.InvalidResourceId);
                        }

                        return true;
                    }

                    if (TryGenerateIdFromKeys(idProperty, out var generatedValue))
                    {
                        resourceId = GetString(idProperty, generatedValue);

                        return true;
                    }

                    resourceId = null;
                    return false;
                }

                private bool TryGenerateIdFromKeys(IProperty idProperty, out object value)
                {
                    var entityEntry = Activator.CreateInstance(_readItemExpression.EntityType.ClrType);

#pragma warning disable EF1001
                    var internalEntityEntry = new InternalEntityEntryFactory().Create(
                        _cosmosQueryContext.Context.GetDependencies().StateManager, _readItemExpression.EntityType, entityEntry);
#pragma warning restore EF1001

                    foreach (var keyProperty in _readItemExpression.EntityType.FindPrimaryKey().Properties)
                    {
                        var property = _readItemExpression.EntityType.FindProperty(keyProperty.Name);

                        if (TryGetParameterValue(property, out var parameterValue))
                        {
                            internalEntityEntry[property] = parameterValue;
                        }
                    }

#pragma warning disable EF1001 // Internal EF Core API usage.
                    internalEntityEntry.SetEntityState(EntityState.Added);
#pragma warning restore EF1001 // Internal EF Core API usage.

                    value = internalEntityEntry[idProperty];

#pragma warning disable EF1001 // Internal EF Core API usage.
                    internalEntityEntry.SetEntityState(EntityState.Detached);
#pragma warning restore EF1001 // Internal EF Core API usage.

                    return value != null;
                }

                private bool TryGetParameterValue(IProperty property, out object value)
                {
                    value = null;
                    return _readItemExpression.PropertyParameters.TryGetValue(property, out var parameterName)
                        && _cosmosQueryContext.ParameterValues.TryGetValue(parameterName, out value);
                }

                private static string GetString(IProperty property, object value)
                {
                    var converter = property.GetTypeMapping().Converter;

                    return converter is null
                        ? (string)value
                        : (string)converter.ConvertToProvider(value);
                }
            }
        }
    }
}
