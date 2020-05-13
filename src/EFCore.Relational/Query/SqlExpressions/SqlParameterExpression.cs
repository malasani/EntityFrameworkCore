// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq.Expressions;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Utilities;

namespace Microsoft.EntityFrameworkCore.Query.SqlExpressions
{
    /// <summary>
    ///     <para>
    ///         An expression that represents a parameter in a SQL tree.
    ///     </para>
    ///     <para>
    ///         This type is typically used by database providers (and other extensions). It is generally
    ///         not used in application code.
    ///     </para>
    /// </summary>
    // Class is sealed because there are no public/protected constructors. Can be unsealed if this is changed.
    public sealed class SqlParameterExpression : SqlExpression
    {
        private readonly ParameterExpression _parameterExpression;

        internal SqlParameterExpression(ParameterExpression parameterExpression, RelationalTypeMapping typeMapping)
            : base(parameterExpression.Type, typeMapping)
        {
            _parameterExpression = parameterExpression;
        }

        /// <summary>
        ///     The name of the parameter.
        /// </summary>
        public string Name => _parameterExpression.Name;

        /// <summary>
        ///     Applies supplied type mapping to this expression.
        /// </summary>
        /// <param name="typeMapping"> A relational type mapping to apply. </param>
        /// <returns> A new expression which has supplied type mapping. </returns>
        public SqlExpression ApplyTypeMapping([CanBeNull] RelationalTypeMapping typeMapping)
            => new SqlParameterExpression(_parameterExpression, typeMapping);

        /// <inheritdoc />
        protected override Expression VisitChildren(ExpressionVisitor visitor)
        {
            Check.NotNull(visitor, nameof(visitor));

            return this;
        }

        /// <inheritdoc />
        public override void Print(ExpressionPrinter expressionPrinter)
        {
            Check.NotNull(expressionPrinter, nameof(expressionPrinter));

            expressionPrinter.Append("@" + _parameterExpression.Name);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
            => obj != null
                && (ReferenceEquals(this, obj)
                    || obj is SqlParameterExpression sqlParameterExpression
                    && Equals(sqlParameterExpression));

        private bool Equals(SqlParameterExpression sqlParameterExpression)
            => base.Equals(sqlParameterExpression)
                && string.Equals(Name, sqlParameterExpression.Name);

        /// <inheritdoc />
        public override int GetHashCode() => HashCode.Combine(base.GetHashCode(), Name);
    }
}
