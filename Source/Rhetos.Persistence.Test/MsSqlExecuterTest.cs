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

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Rhetos.TestCommon;
using Rhetos.Utilities;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;

namespace Rhetos.Persistence.Test
{
    [TestClass]
    [DeploymentItem("ConnectionStrings.config")]
    public class MsSqlExecuterTest
    {
        private static MsSqlExecuter NewSqlExecuter(string connectionString = null, IUserInfo testUser = null)
        {
            connectionString = connectionString ?? SqlUtility.ConnectionString;
            testUser = testUser ?? new NullUserInfo();
            return new MsSqlExecuter(connectionString, new ConsoleLogProvider(), testUser, null);
        }

        private static MsSqlExecuter NewSqlExecuter(IUserInfo testUser)
        {
            return NewSqlExecuter(null, testUser);
        }

        private string GetRandomTableName()
        {
            NewSqlExecuter().ExecuteSql(new[] { "IF SCHEMA_ID('RhetosUnitTest') IS NULL EXEC('CREATE SCHEMA RhetosUnitTest')" });
            var newTableName = "RhetosUnitTest.T" + Guid.NewGuid().ToString().Replace("-", "");
            Console.WriteLine("Generated random table name: " + newTableName);
            return newTableName;
        }

        [TestInitialize]
        public void ChecklDatabaseAvailability()
        {
            TestUtility.CheckDatabaseAvailability("MsSql");
        }

        [ClassCleanup]
        public static void DropRhetosUnitTestSchema()
        {
            try { TestUtility.CheckDatabaseAvailability("MsSql"); }
            catch { return; }

            NewSqlExecuter().ExecuteSql(new[] { 
                @"DECLARE @sql NVARCHAR(MAX)
                    SET @sql = ''
                    SELECT @sql = @sql + 'DROP TABLE RhetosUnitTest.' + QUOTENAME(name) + ';' + CHAR(13) + CHAR(10)
                        FROM sys.tables WHERE schema_id = SCHEMA_ID('RhetosUnitTest')
                    EXEC (@sql)",
                "IF SCHEMA_ID('RhetosUnitTest') IS NOT NULL DROP SCHEMA RhetosUnitTest" });
        }

        [TestMethod()]
        public void ExecuteSql_SaveLoadTest()
        {
            MsSqlExecuter sqlExecuter = NewSqlExecuter();
            string table = GetRandomTableName();

            IEnumerable<string> commands = new[]
                {
                    "CREATE TABLE " + table + " ( A INTEGER )",
                    "INSERT INTO " + table + " SELECT 123"
                };
            sqlExecuter.ExecuteSql(commands);
            int actual = 0;
            sqlExecuter.ExecuteReader("SELECT * FROM " + table, dr => actual = dr.GetInt32(0));
            Assert.AreEqual(123, actual);
        }

        [TestMethod()]
        public void ExecuteSql_SimpleSqlError()
        {
            TestUtility.ShouldFail(() => NewSqlExecuter().ExecuteSql(new[] { "raiserror('aaa', 16, 100)" }),
                "aaa", "16", "100");
        }

        [TestMethod()]
        public void ExecuteSql_InfoMessageIsNotError()
        {
            NewSqlExecuter().ExecuteSql(new[] { "raiserror('aaa', 0, 100)" }); // Exception not expected here.
        }

        [TestMethod()]
        public void ExecuteSql_ErrorDescriptions()
        {
            MsSqlExecuter sqlExecuter = NewSqlExecuter();
              
            IEnumerable<string> commands = new[]
                {
@"print 'xxx'
raiserror('aaa', 0, 100)
raiserror('bbb', 1, 101)
raiserror('ccc', 10, 110)
raiserror('ddd', 16, 116)
raiserror('eee', 17, 117)
raiserror('fff', 18, 118)"
                };

            var expectedStrings = new[] { "aaa", "bbb", "ccc", "ddd", "eee", "fff", "101", "116", "117", "118" }; // Error of severity 0 and 10 (states "100" and "110") do not require detailed error message.

            TestUtility.ShouldFail(() => sqlExecuter.ExecuteSql(commands), expectedStrings);
        }

        [TestMethod()]
        public void ExecuteSql_LoginError()
        {
            string nonexistentDatabase = "db" + Guid.NewGuid().ToString().Replace("-", "");

            var connectionStringBuilder = new SqlConnectionStringBuilder(SqlUtility.ConnectionString);
            connectionStringBuilder.InitialCatalog = nonexistentDatabase;
            connectionStringBuilder.IntegratedSecurity = true;
            string nonexistentDatabaseConnectionString = connectionStringBuilder.ConnectionString;

            MsSqlExecuter sqlExecuter = NewSqlExecuter(nonexistentDatabaseConnectionString);
            TestUtility.ShouldFail(() => sqlExecuter.ExecuteSql(new[] { "print 123" }),
                connectionStringBuilder.DataSource, connectionStringBuilder.InitialCatalog, Environment.UserName);
        }

        [TestMethod()]
        public void ExecuteSql_CommitImmediately()
        {
            string table = GetRandomTableName();
            NewSqlExecuter().ExecuteSql(new[] {
                "create table " + table + " ( a integer )" });
            NewSqlExecuter().ExecuteSql(new[] {
                "set lock_timeout 0",
                "select * from " + table }); // Exception not expected here.
        }

        [TestMethod()]
        public void ExecuteSql_RollbackImmediately()
        {
            string table = GetRandomTableName();
            try
            {
                NewSqlExecuter().ExecuteSql(new[] {
                    "create table " + table + " ( a integer )",
                    "create table forcederror" });
            }
            catch
            {
            }
            NewSqlExecuter().ExecuteSql(new[] {
                "set lock_timeout 0",
                "create table " + table + " ( a integer )" }); // Lock timeout exception not expected here.
        }

        [TestMethod()]
        public void ExecuteSql_StopExecutingScriptsAfterError()
        {
            string table = GetRandomTableName();
            try
            {
                NewSqlExecuter().ExecuteSql(new[] {
                    "create table forcederror",
                    "create table " + table + " ( a integer )" });
            }
            catch
            {
            }
            NewSqlExecuter().ExecuteSql(new[] {
                "create table " + table + " ( a integer )" }); // Exception not expected here.
        }

        [TestMethod()]
        public void ExecuteSql_RollbackedTransaction()
        {
            try
            {
                NewSqlExecuter().ExecuteSql(new[] {
                    "rollback",
                    "raiserror('abc', 16, 10)" });
            }
            catch (Exception e)
            {
                Assert.IsFalse(e.Message.Contains("abc"), "Script executed out of transaction. Second query must not be executed.");
                Assert.IsTrue(e.Message.ToLower().Contains("transaction"), "Error cause is not described.");
            }
        }

        [TestMethod()]
        public void ExecuteSql_TransactionLevel()
        {
            try
            {
                NewSqlExecuter().ExecuteSql(new[] {
                    "begin tran",
                    "raiserror('abc', 16, 10)" });
            }
            catch (Exception e)
            {
                Assert.IsFalse(e.Message.Contains("abc"), "Change of transaction level is not recognized. Second query must not be executed.");
                Assert.IsTrue(e.Message.ToLower().Contains("transaction"), "Error cause is not described.");
            }
        }

        [TestMethod]
        public void SendUserInfoInSqlContext_NoUser()
        {
            var testUser = new TestUserInfo(null, null, false);
            var sqlExecuter = NewSqlExecuter(SqlUtility.ConnectionString, testUser);
            var result = new List<object>();
            sqlExecuter.ExecuteReader("SELECT context_info()", reader => result.Add(reader[0]));
            Console.WriteLine(result.Single());
            Assert.AreEqual(typeof(DBNull), result.Single().GetType());
        }

        [TestMethod]
        public void SendUserInfoInSqlContext_ReadWithUser()
        {
            var testUser = new TestUserInfo("Bob", "HAL9000");
            var sqlExecuter = NewSqlExecuter(testUser);
            var result = new List<string>();
            sqlExecuter.ExecuteReader(
                @"SELECT (CONVERT([varchar](128),left(context_info(),isnull(nullif(charindex(0x00,context_info())-(1),(-1)),(128))),(0)))",
                reader => result.Add(reader[0].ToString()));
            Console.WriteLine(result.Single());
            Assert.IsTrue(result.Single().Contains(testUser.UserName), "context_info should contain username.");
            Assert.IsTrue(result.Single().Contains(testUser.Workstation), "context_info should contain client workstation.");
        }

        [TestMethod]
        public void SendUserInfoInSqlContext_WriteWithUser()
        {
            var testUser = new TestUserInfo("Bob", "HAL9000");
            var sqlExecuter = NewSqlExecuter(testUser);
            string table = GetRandomTableName();

            var result = new List<string>();
            sqlExecuter.ExecuteSql(new [] {
                @"SELECT Context = (CONVERT([varchar](128),left(context_info(),isnull(nullif(charindex(0x00,context_info())-(1),(-1)),(128))),(0))) INTO " + table});

            sqlExecuter.ExecuteReader(
                @"SELECT * FROM " + table,
                reader => result.Add(reader[0].ToString()));

            Console.WriteLine(result.Single());
            Assert.IsTrue(result.Single().Contains(testUser.UserName), "context_info should contain username.");
            Assert.IsTrue(result.Single().Contains(testUser.Workstation), "context_info should contain client workstation.");
        }
    }
}