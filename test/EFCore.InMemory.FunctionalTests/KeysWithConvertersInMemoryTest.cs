// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using Microsoft.EntityFrameworkCore.TestUtilities;

namespace Microsoft.EntityFrameworkCore
{
    public class KeysWithConvertersInMemoryTest : KeysWithConvertersTestBase<KeysWithConvertersInMemoryTest.KeysWithConvertersInMemoryFixture>
    {
        public KeysWithConvertersInMemoryTest(KeysWithConvertersInMemoryFixture fixture)
            : base(fixture)
        {
        }

        public class KeysWithConvertersInMemoryFixture : KeysWithConvertersFixtureBase
        {
            protected override ITestStoreFactory TestStoreFactory
                => InMemoryTestStoreFactory.Instance;
        }
    }
}
