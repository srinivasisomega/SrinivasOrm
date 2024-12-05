using System;
using System.Collections.Generic;
using System.Data;
using Microsoft.Data.SqlClient;
using System.Linq;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;

namespace SrinivasOrm
{ 
    
        public class DbContext
        {
            private readonly string _connectionString;

            public DbContext(string connectionString)
            {
                _connectionString = connectionString;
            }

        /// <summary>
        /// Creates tables based on the model classes with primary keys, foreign keys, and constraints.
        /// </summary>
public void CreateTables(params Type[] modelTypes)
{
    using var connection = new SqlConnection(_connectionString);
    connection.Open();

    var tableCreationQueries = new Dictionary<string, string>();
    var foreignKeyConstraints = new List<string>();

    foreach (var modelType in modelTypes)
    {
        var tableName = modelType.Name;
        var properties = modelType.GetProperties();

        var createTableQuery = $"CREATE TABLE {tableName} (";
        foreach (var prop in properties)
        {
            var columnName = prop.Name;
            var columnType = GetSqlDbType(prop); // Pass PropertyInfo

            createTableQuery += $"{columnName} {columnType}";

            if (prop.GetCustomAttribute<PrimaryKeyAttribute>() != null)
                createTableQuery += " PRIMARY KEY";

            if (prop.GetCustomAttribute<UniqueAttribute>() != null)
                createTableQuery += " UNIQUE";

            if (!prop.GetCustomAttribute<NullableAttribute>()?.Equals(null) ?? true)
                createTableQuery += " NOT NULL";

            createTableQuery += ",";

            // Collect foreign key constraints
            if (prop.GetCustomAttribute<ForeignKeyAttribute>() is ForeignKeyAttribute foreignKeyAttr)
            {
                var fkConstraint =
                    $"ALTER TABLE {tableName} ADD CONSTRAINT FK_{tableName}_{foreignKeyAttr.ReferenceTable} " +
                    $"FOREIGN KEY ({columnName}) REFERENCES {foreignKeyAttr.ReferenceTable}({foreignKeyAttr.ReferenceColumn})";
                foreignKeyConstraints.Add(fkConstraint);
            }
        }

        createTableQuery = createTableQuery.TrimEnd(',') + ");";
        tableCreationQueries[tableName] = createTableQuery;
    }

    // Create all tables first
    foreach (var query in tableCreationQueries.Values)
    {
        ExecuteQuery(connection, query);
    }

    // Add foreign key constraints
    foreach (var constraint in foreignKeyConstraints)
    {
        ExecuteQuery(connection, constraint);
    }
}
        public void SyncTables(params Type[] modelTypes)
        {
            using var connection = new SqlConnection(_connectionString);
            connection.Open();

            foreach (var modelType in modelTypes)
            {
                var tableName = modelType.Name;
                var properties = modelType.GetProperties();

                // Get existing columns and constraints
                var existingColumns = GetTableColumns(connection, tableName);
                var primaryKeyConstraint = GetPrimaryKeyConstraint(connection, tableName);
                var foreignKeyConstraints = GetForeignKeyConstraints(connection, tableName);

                foreach (var prop in properties)
                {
                    var columnName = prop.Name;
                    var columnType = GetSqlDbType(prop);
                    var isNullable = !prop.GetCustomAttributes<NotNullAttribute>().Any(); // Assuming you use NotNullAttribute for nullable settings

                    if (!existingColumns.Contains(columnName))
                    {
                        // Add new column
                        var addColumnQuery = $"ALTER TABLE {tableName} ADD {columnName} {columnType} {(isNullable ? "NULL" : "NOT NULL")}";
                        ExecuteQuery(connection, addColumnQuery);
                    }
                    else
                    {
                        // Drop constraints related to the column
                        DropColumnConstraints(connection, tableName, columnName, primaryKeyConstraint, foreignKeyConstraints);

                        // Alter column type and nullability
                        var alterColumnQuery = $"ALTER TABLE {tableName} ALTER COLUMN {columnName} {columnType} {(isNullable ? "NULL" : "NOT NULL")}";
                        ExecuteQuery(connection, alterColumnQuery);

                        // Reapply primary key if needed
                        if (primaryKeyConstraint != null && columnName == "Id")
                        {
                            var addPrimaryKeyQuery = $"ALTER TABLE {tableName} ADD CONSTRAINT {primaryKeyConstraint} PRIMARY KEY ({columnName})";
                            ExecuteQuery(connection, addPrimaryKeyQuery);
                        }
                    }

                    // Reapply constraints (Foreign Key, Unique, Check)
                    ApplyConstraints(connection, tableName, columnName, prop);
                }
            }
        }

        private static void DropColumnConstraints(SqlConnection connection, string tableName, string columnName, string primaryKeyConstraint, IEnumerable<string> foreignKeyConstraints)
        {
            // Drop foreign keys related to this column
            foreach (var fkConstraint in foreignKeyConstraints)
            {
                var dropForeignKeyQuery = $"ALTER TABLE {tableName} DROP CONSTRAINT {fkConstraint}";
                ExecuteQuery(connection, dropForeignKeyQuery);
            }

            // Drop primary key if this column is part of it
            if (!string.IsNullOrEmpty(primaryKeyConstraint))
            {
                var dropPrimaryKeyQuery = $"ALTER TABLE {tableName} DROP CONSTRAINT {primaryKeyConstraint}";
                ExecuteQuery(connection, dropPrimaryKeyQuery);
            }
        }

        private static void ApplyConstraints(SqlConnection connection, string tableName, string columnName, PropertyInfo prop)
        {
            // Handle Foreign Key constraint
            if (prop.GetCustomAttribute<ForeignKeyAttribute>() is ForeignKeyAttribute foreignKeyAttr)
            {
                var fkConstraint =
                    $"ALTER TABLE {tableName} ADD CONSTRAINT FK_{tableName}_{foreignKeyAttr.ReferenceTable} " +
                    $"FOREIGN KEY ({columnName}) REFERENCES {foreignKeyAttr.ReferenceTable}({foreignKeyAttr.ReferenceColumn})";
                ExecuteQuery(connection, fkConstraint);
            }

            // Handle Unique constraint
            if (prop.GetCustomAttribute<UniqueAttribute>() != null)
            {
                var uniqueConstraint =
                    $"ALTER TABLE {tableName} ADD CONSTRAINT UQ_{tableName}_{columnName} UNIQUE ({columnName})";
                ExecuteQuery(connection, uniqueConstraint);
            }

            // Handle Check constraint
            if (prop.GetCustomAttribute<CheckConstraintAttribute>() is CheckConstraintAttribute checkConstraintAttr)
            {
                var checkConstraint =
                    $"ALTER TABLE {tableName} ADD CONSTRAINT CK_{tableName}_{columnName} CHECK ({checkConstraintAttr.Condition})";
                ExecuteQuery(connection, checkConstraint);
            }
        }

        private static string GetPrimaryKeyConstraint(SqlConnection connection, string tableName)
        {
            var query = $@"
        SELECT kc.CONSTRAINT_NAME
        FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
        INNER JOIN INFORMATION_SCHEMA.KEY_COLUMN_USAGE AS kc
        ON tc.CONSTRAINT_NAME = kc.CONSTRAINT_NAME
        WHERE tc.TABLE_NAME = '{tableName}' AND tc.CONSTRAINT_TYPE = 'PRIMARY KEY'";

            using var command = new SqlCommand(query, connection);
            using var reader = command.ExecuteReader();

            return reader.Read() ? reader.GetString(0) : null;
        }

        private static IEnumerable<string> GetForeignKeyConstraints(SqlConnection connection, string tableName)
        {
            var query = $@"
        SELECT CONSTRAINT_NAME
        FROM INFORMATION_SCHEMA.REFERENTIAL_CONSTRAINTS AS rc
        INNER JOIN INFORMATION_SCHEMA.TABLE_CONSTRAINTS AS tc
        ON rc.CONSTRAINT_NAME = tc.CONSTRAINT_NAME
        WHERE tc.TABLE_NAME = '{tableName}'";

            using var command = new SqlCommand(query, connection);
            using var reader = command.ExecuteReader();

            var constraints = new List<string>();
            while (reader.Read())
            {
                constraints.Add(reader.GetString(0));
            }

            return constraints;
        }




        /// <summary>
        /// Adds a record to a specified table.
        /// </summary>
        public void AddRecord<T>(T record)
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();

                var tableName = typeof(T).Name;
                var properties = typeof(T).GetProperties();
                var columns = string.Join(", ", properties.Select(p => p.Name));
                var values = string.Join(", ", properties.Select(p => $"@{p.Name}"));

                var insertQuery = $"INSERT INTO {tableName} ({columns}) VALUES ({values})";
                using var command = new SqlCommand(insertQuery, connection);

                foreach (var prop in properties)
                {
                    command.Parameters.AddWithValue($"@{prop.Name}", prop.GetValue(record) ?? DBNull.Value);
                }

                command.ExecuteNonQuery();
            }

        private static string GetSqlDbType(PropertyInfo property)
        {
            var type = property.PropertyType;

            if (type == typeof(int)) return "INT";
            if (type == typeof(string))
            {
                // Use a default length of 255 for strings used in constraints
                var isConstrained = property.GetCustomAttribute<UniqueAttribute>() != null ||
                                    property.GetCustomAttribute<PrimaryKeyAttribute>() != null;
                return isConstrained ? "NVARCHAR(255)" : "NVARCHAR(MAX)";
            }
            if (type == typeof(DateTime)) return "DATETIME";
            if (type == typeof(bool)) return "BIT";

            throw new NotSupportedException($"Type {type.Name} is not supported");
        }


        private static void ExecuteQuery(SqlConnection connection, string query)
            {
                using var command = new SqlCommand(query, connection);
                command.ExecuteNonQuery();
            }

            private static HashSet<string> GetTableColumns(SqlConnection connection, string tableName)
            {
                var columns = new HashSet<string>();
                var query = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}'";
                using var command = new SqlCommand(query, connection);
                using var reader = command.ExecuteReader();

                while (reader.Read())
                {
                    columns.Add(reader.GetString(0));
                }

                return columns;
            }
        }

        #region Attributes
        [AttributeUsage(AttributeTargets.Property)]
        public class PrimaryKeyAttribute : Attribute { }

        [AttributeUsage(AttributeTargets.Property)]
        public class ForeignKeyAttribute : Attribute
        {
            public string ReferenceTable { get; }
            public string ReferenceColumn { get; }

            public ForeignKeyAttribute(string referenceTable, string referenceColumn)
            {
                ReferenceTable = referenceTable;
                ReferenceColumn = referenceColumn;
            }
        }

        [AttributeUsage(AttributeTargets.Property)]
        public class NullableAttribute : Attribute { }

        [AttributeUsage(AttributeTargets.Property)]
        public class UniqueAttribute : Attribute { }

        [AttributeUsage(AttributeTargets.Property)]
        public class CheckConstraintAttribute : Attribute
        {
            public string Condition { get; }

            public CheckConstraintAttribute(string condition)
            {
                Condition = condition;
            }
        }
        #endregion
}


