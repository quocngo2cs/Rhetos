﻿/*
    Copyright (C) 2014 Omega software d.o.o.

    This file is part of Rhetos.

    This program is free software: you can redistribute it and/or modify
    it under the terms of the GNU Affero General Public License as
    published by the Free Software Foundation, either version 3 of the
    License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU Affero General Public License for more details.

    You should have received a copy of the GNU Affero General Public License
    along with this program.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos.TestCommon;
using Rhetos.Configuration.Autofac;
using Rhetos.Utilities;
using Rhetos.Dom.DefaultConcepts;

namespace CommonConcepts.Test.OldConcepts
{
    // TODO: Make better unit tests. These are mostly research spikes, not unit tests.

    [TestClass]
    public class BrowseTest
    {
        [TestMethod]
        public void SimpleReference()
        {
            using (var container = new RhetosTestContainer())
            {
                var repository = container.Resolve<Common.DomRepository>();

                Guid refID = Guid.NewGuid();
                container.Resolve<ISqlExecuter>().ExecuteSql(new[]
                    {
                        "DELETE FROM TestBrowse.Source;",
                        "DELETE FROM TestBrowse.Other;",
                        "INSERT INTO TestBrowse.Other (ID, Name) SELECT '" + refID + "', 'abc';",
                        "INSERT INTO TestBrowse.Source (RefID) SELECT '" + refID + "';",
                    });

                Assert.AreEqual("abc", repository.TestBrowse.Source.Query().ToArray().Select(item => item.Ref != null ? item.Ref.Name : null).Single(), "separated loading with null checking");
                Assert.AreEqual("abc", repository.TestBrowse.Source.Query().Select(item => item.Ref != null ? item.Ref.Name : null).Single(), "all in one query with null checking");

                Assert.AreEqual("abc", repository.TestBrowse.Source.Query().Select(item => item.Ref.Name).Single(), "all in one query");
                Assert.AreEqual("abc", repository.TestBrowse.Source.Query().ToArray().Select(item => item.Ref.Name).Single(), "separated loading");
            }
        }

        [TestMethod]
        [Ignore]
        public void NullReference()
        {
            using (var container = new RhetosTestContainer())
            {
                var repository = container.Resolve<Common.DomRepository>();

                Guid refID = Guid.NewGuid();
                container.Resolve<ISqlExecuter>().ExecuteSql(new[]
                    {
                        "DELETE FROM TestBrowse.Source;",
                        "DELETE FROM TestBrowse.Other;",
                        "INSERT INTO TestBrowse.Other (ID, Name) SELECT '" + refID + "', 'abc';",
                        "INSERT INTO TestBrowse.Source (RefID) SELECT NULL;"
                    });

                Assert.IsNull(repository.TestBrowse.Source.Query().ToArray().Select(item => item.Ref != null ? item.Ref.Name : null).Single(), "separated loading with null checking");
                Assert.IsNull(repository.TestBrowse.Source.Query().Select(item => item.Ref != null ? item.Ref.Name : null).Single(), "all in one query with null checking");

                Assert.IsNull(repository.TestBrowse.Source.Query().Select(item => item.Ref.Name).Single(), "all in one query");

                // TODO: "'Separated loading' fails because LINQ2NH will handle nullable properties and null values differently than a simple LINQ query over materialized instances (Linq2Objects). Try to implement browse in a such way that it behaves the same in both scenarios without degrading performance (maybe generating SqlView).
            
                Assert.IsNull(repository.TestBrowse.Source.Query().ToArray().Select(item => item.Ref.Name).Single(), "separated loading");
            }
        }

        [TestMethod]
        public void ReuseableSourceFilter()
        {
            using (var container = new RhetosTestContainer())
            {
                var repository = container.Resolve<Common.DomRepository>();

                Guid refID = Guid.NewGuid();
                container.Resolve<ISqlExecuter>().ExecuteSql(new[]
                    {
                        "DELETE FROM TestBrowse.Source;",
                        "DELETE FROM TestBrowse.Other;",
                        "INSERT INTO TestBrowse.Other (ID, Name) SELECT '" + refID + "', 'abc';",
                        "INSERT INTO TestBrowse.Source (Code, RefID) SELECT '1', '" + refID + "';",
                        "INSERT INTO TestBrowse.Source (Code, RefID) SELECT '2', NULL;"
                    });

                var source = repository.TestBrowse.Source.Query();

                var sf = GenerateBrowse(FilterSource(source, new FilterParameters { Code = "1" }));

                Assert.AreEqual("abc", string.Join(", ", sf.ToArray().Select(item => item.RefName)));
            }
        }

        class FilterParameters
        {
            public FilterParameters()
            {
                RefID = "00000000-0000-0000-0000-000000000000";
            }

            public string Code { get; set; }
            public string RefID { get; set; }
        }

        static IQueryable<Common.Queryable.TestBrowse_Source> FilterSource(IQueryable<Common.Queryable.TestBrowse_Source> source, FilterParameters parameters)
        {
            return source.Where(item =>
                parameters.Code == null || parameters.Code == item.Code
                && parameters.RefID == "00000000-0000-0000-0000-000000000000" || new Guid(parameters.RefID) == item.RefID);
        }

        static IQueryable<TestBrowse.SF> GenerateBrowse(IQueryable<Common.Queryable.TestBrowse_Source> source)
        {
            return source.Select(item => new TestBrowse.SF { RefName = item.Ref.Name });
        }

        [TestMethod]
        public void MultiplePropertiesSameSource()
        {
            using (var container = new RhetosTestContainer())
            {
                var repository = container.Resolve<Common.DomRepository>();

                Guid refID = Guid.NewGuid();
                container.Resolve<ISqlExecuter>().ExecuteSql(new[]
                    {
                        "DELETE FROM TestBrowse.Source",
                        "DELETE FROM TestBrowse.Other",
                        "INSERT INTO TestBrowse.Other (ID, Name) VALUES ('" + refID + "', 'abc')",
                        "INSERT INTO TestBrowse.Source (RefID, Code) VALUES ('" + refID + "', '123')",
                    });

                Assert.AreEqual("123 123 abc abc", TestUtility.DumpSorted(
                    repository.TestBrowse.SFMulti.All(), item => item.Code1 + " " + item.Code2 + " " + item.RefName1 + " " + item.RefName2));
            }
        }

        [TestMethod]
        public void TakeComplex()
        {
            using (var container = new RhetosTestContainer())
            {
                var repository = container.Resolve<Common.DomRepository>();

                Guid parentID = Guid.NewGuid();
                container.Resolve<ISqlExecuter>().ExecuteSql(new[]
                    {
                        "DELETE FROM TestBrowse.Complex",
                        "DELETE FROM TestBrowse.Parent",
                        "DELETE FROM TestBrowse.ParentBase",
                        "INSERT INTO TestBrowse.ParentBase (ID, Name) VALUES ('" + parentID + "', 'base')",
                        "INSERT INTO TestBrowse.Parent (ID, Name) VALUES ('" + parentID + "', 'parent')",
                        "INSERT INTO TestBrowse.ParentExtension2 (ID, Name2) VALUES ('" + parentID + "', 'ext2')",
                        "INSERT INTO TestBrowse.Complex (RefID, Code) VALUES ('" + parentID + "', 'complex')",
                    });

                Assert.AreEqual("complex parent parent parent base ext2", TestUtility.DumpSorted(
                    repository.TestBrowse.SFTake.Query(),
                    item => item.Code + " " + item.RefName + " " + item.RefName2 + " " + item.RefName3 + " " + item.RefBaseName + " " + item.RefExtension_ParentExtension2Name2));

                Assert.AreEqual("complex", TestUtility.DumpSorted(repository.TestBrowse.SFTake.Query(), item => item.Base.Code));

                Assert.AreEqual(parentID, repository.TestBrowse.SFTake.Query().Single().RefID);
                Assert.AreEqual(parentID, repository.TestBrowse.SFTake.Query().Single().ParentReferenceID);
                Assert.AreEqual("parent", repository.TestBrowse.SFTake.Query().Single().ParentReference.Name);
            }
        }

        [TestMethod]
        public void Filters()
        {
            using (var container = new RhetosTestContainer())
            {
                var repository = container.Resolve<Common.DomRepository>();
                var genericRepository = container.Resolve<GenericRepository<TestBrowse.SF>>();

                //Assert.AreEqual(0, genericRepository.Load(item => item.ID == Guid.Empty).Count(), "Generic repository: Simple loader with filter expression.");
                //Assert.AreEqual(0, repository.TestBrowse.SF.Query().Where(item => item.ID == Guid.Empty).ToSimple().ToList().Count(), "Simple query.");
                //Assert.AreEqual(0, repository.TestBrowse.SF.Load(item => item.ID == Guid.Empty).Count(), "Simple loader with filter expression."); // Same query as above, using different infrastructure

                Guid sourceId = Guid.NewGuid();
                Guid refId = Guid.NewGuid();
                container.Resolve<ISqlExecuter>().ExecuteSql(new[]
                    {
                        "DELETE FROM TestBrowse.Source;",
                        "DELETE FROM TestBrowse.Other;",
                        "INSERT INTO TestBrowse.Other (ID, Name) SELECT '" + refId + "', 'abc';",
                        "INSERT INTO TestBrowse.Source (ID, RefID) SELECT '" + sourceId + "', '" + refId + "';",
                    });

                var ids = new[] { sourceId };
                Assert.AreEqual("abc", repository.TestBrowse.SF.Filter(ids).Single().RefName);
                Assert.AreEqual("abc", repository.TestBrowse.SF.Query().Where(item => ids.Contains(item.ID)).Single().RefName);

                {
                    Assert.AreEqual("abc", genericRepository.Load(ids).Single().RefName);

                    var q = genericRepository.Query(ids);
                    Assert.IsTrue(q is IQueryable, q.GetType().FullName);
                    Assert.AreEqual("abc", q.Single().RefName);
                }

                var manyIds = Enumerable.Range(0, 5000).Select(x => Guid.NewGuid()).Concat(ids).ToList();

                {
                    Assert.AreEqual("abc", genericRepository.Load(manyIds).Single().RefName);

                    var q = genericRepository.Query(manyIds);
                    Assert.IsTrue(q is IQueryable, q.GetType().FullName);
                    Assert.AreEqual("abc", q.Single().RefName);
                }
            }
        }

        [TestMethod]
        public void OtherModule()
        {
            using (var container = new RhetosTestContainer())
            {
                var repository = container.Resolve<Common.DomRepository>();

                Guid parentID = Guid.NewGuid();
                container.Resolve<ISqlExecuter>().ExecuteSql(new[]
                    {
                        "DELETE FROM TestBrowse.Complex",
                        "DELETE FROM TestBrowse.Parent",
                        "DELETE FROM TestBrowse.ParentBase",
                        "INSERT INTO TestBrowse.ParentBase (ID, Name) VALUES ('" + parentID + "', 'base')",
                        "INSERT INTO TestBrowse.Parent (ID, Name) VALUES ('" + parentID + "', 'parent')",
                        "INSERT INTO TestBrowse.ParentExtension2 (ID, Name2) VALUES ('" + parentID + "', 'ext2')",
                        "INSERT INTO TestBrowse.Complex (RefID, Code) VALUES ('" + parentID + "', 'complex')",
                    });

                Assert.AreEqual("complex parent parent parent base ext2", TestUtility.DumpSorted(
                    repository.TestBrowse2.OtherModuleBrowse.Query(),
                    item => item.Code + " " + item.RefName + " " + item.RefName2 + " " + item.RefName3 + " " + item.RefBaseName + " " + item.RefExtension_ParentExtension2Name2));

                Assert.AreEqual("complex", TestUtility.DumpSorted(repository.TestBrowse2.OtherModuleBrowse.Query(), item => item.Base.Code));

                Assert.AreEqual(parentID, repository.TestBrowse2.OtherModuleBrowse.Query().Single().RefID);
                Assert.AreEqual(parentID, repository.TestBrowse2.OtherModuleBrowse.Query().Single().ParentReferenceID);
                Assert.AreEqual("parent", repository.TestBrowse2.OtherModuleBrowse.Query().Single().ParentReference.Name);
            }
        }
    }
}
