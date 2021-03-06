﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using Microsoft.CodeAnalysis.SQLite.Interop;
using Microsoft.CodeAnalysis.Storage;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.SQLite
{
    internal partial class SQLitePersistentStorage
    {
        private readonly ConcurrentDictionary<string, int> _stringToIdMap = new ConcurrentDictionary<string, int>();

        private void FetchStringTable(SqlConnection connection)
        {
            using (var resettableStatement = connection.GetResettableStatement(
                $@"select * from ""{StringInfoTableName}"""))
            {
                var statement = resettableStatement.Statement;
                while (statement.Step() == Result.ROW)
                {
                    var id = statement.GetInt32At(columnIndex: 0);
                    var value = statement.GetStringAt(columnIndex: 1);

                    // Note that TryAdd won't overwrite an existing string->id pair.  That's what
                    // we want.  we don't want the strings we've allocated from the DB to be what
                    // we hold onto.  We'd rather hold onto the strings we get from sources like
                    // the workspaces.  This helps avoid unnecessary string instance duplication.
                    _stringToIdMap.TryAdd(value, id);
                }
            }
        }

        private int? TryGetStringId(SqlConnection connection, string value)
        {
            // First see if we've cached the ID for this value locally.  If so, just return
            // what we already have.
            if (_stringToIdMap.TryGetValue(value, out int existingId))
            {
                return existingId;
            }

            // Otherwise, try to get or add the string to the string table in the database.
            var id = TryGetStringIdFromDatabase(connection, value);
            if (id != null)
            {
                _stringToIdMap[value] = id.Value;
            }

            return id;
        }

        private int? TryGetStringIdFromDatabase(SqlConnection connection, string value)
        {
            // First, check if we can find that string in the string table.
            var stringId = TryGetStringIdFromDatabaseWorker(connection, value, canReturnNull: true);
            if (stringId != null)
            {
                // Found the value already in the db.  Another process (or thread) might have added it.
                // We're done at this point.
                return stringId;
            }

            // The string wasn't in the db string table.  Add it.  Note: this may fail if some
            // other thread/process beats us there as this table has a 'unique' constraint on the
            // values.
            try
            {
                connection.RunInTransaction(() =>
                {
                    stringId = InsertStringIntoDatabase_MustRunInTransaction(connection, value);
                });

                Contract.ThrowIfTrue(stringId == null);
                return stringId;
            }
            catch (SqlException ex) when (ex.Result == Result.CONSTRAINT)
            {
                // We got a constraint violation.  This means someone else beat us to adding this
                // string to the string-table.  We should always be able to find the string now.
                stringId = TryGetStringIdFromDatabaseWorker(connection, value, canReturnNull: false);
                return stringId;
            }
            catch (Exception ex)
            {
                // Some other error occurred.  Log it and return nothing.
                StorageDatabaseLogger.LogException(ex);
            }

            return null;
        }

        private static int InsertStringIntoDatabase_MustRunInTransaction(SqlConnection connection, string value)
        {
            if (!connection.IsInTransaction)
            {
                throw new InvalidOperationException("Must call this while connection has transaction open");
            }

            int id = -1;

            using (var resettableStatement = connection.GetResettableStatement(
                $@"insert into ""{StringInfoTableName}""(""{DataColumnName}"") values (?)"))
            {
                var statement = resettableStatement.Statement;

                // SQLite bindings are 1-based.
                statement.BindStringParameter(parameterIndex: 1, value: value);

                // Try to insert the value.  This may throw a constraint exception if some
                // other process beat us to this string.
                statement.Step();

                // Successfully added the string.  The ID for it can be retrieved as the LastInsertRowId
                // for the db.  This is also safe to call because we must be in a transaction when this
                // is invoked.
                id = connection.LastInsertRowId();
            }

            Contract.ThrowIfTrue(id == -1);
            return id;
        }

        private int? TryGetStringIdFromDatabaseWorker(
            SqlConnection connection, string value, bool canReturnNull)
        {
            try
            {
                using (var resettableStatement = connection.GetResettableStatement(
                    $@"select * from ""{StringInfoTableName}"" where (""{DataColumnName}"" = ?) limit 1"))
                {
                    var statement = resettableStatement.Statement;

                    // SQLite's binding indices are 1-based. 
                    statement.BindStringParameter(parameterIndex: 1, value: value);

                    var stepResult = statement.Step();
                    if (stepResult == Result.ROW)
                    {
                        return statement.GetInt32At(columnIndex: 0);
                    }
                }
            }
            catch (Exception ex)
            {
                // If we simply failed to even talk to the DB then we have to bail out.  There's
                // nothing we can accomplish at this point.
                StorageDatabaseLogger.LogException(ex);
                return null;
            }

            // No item with this value in the table.
            if (canReturnNull)
            {
                return null;
            }

            // This should not be possible.  We only called here if we got a constraint violation.
            // So how could we then not find the string in the table?
            throw new InvalidOperationException();
        }
    }
}