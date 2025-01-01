﻿using Npgsql;
using Elsa.Sql.Client;
using System.Data;

namespace Elsa.Sql.PostgreSql;

public class PostgreSqlClient : BaseSqlClient, ISqlClient
{
    private string? _connectionString;

    /// <summary>
    /// PostgreSQL client implimentation.
    /// </summary>
    /// <param name="connectionString"></param>
    public PostgreSqlClient(string? connectionString) => _connectionString = connectionString;

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async Task<int?> ExecuteCommandAsync(string sqlCommand)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        var command = new NpgsqlCommand(sqlCommand, connection);

        var result = await command.ExecuteNonQueryAsync();
        return result;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async Task<object?> ExecuteScalarAsync(string sqlQuery)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        var command = new NpgsqlCommand(sqlQuery, connection);

        var result = await command.ExecuteScalarAsync();
        return result;
    }

    /// <summary>
    /// <inheritdoc/>
    /// </summary>
    public async Task<DataSet?> ExecuteQueryAsync(string sqlQuery)
    {
        using var connection = new NpgsqlConnection(_connectionString);
        connection.Open();
        var command = new NpgsqlCommand(sqlQuery, connection);

        using var reader = await command.ExecuteReaderAsync();
        return await Task.FromResult(ReadAsDataSet(reader));
    }
}