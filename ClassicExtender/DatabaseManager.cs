using Npgsql;
using System.Data;

namespace DatabaseManager
{
    public class DatabaseConnectionManager
    {
        private readonly string _connectionString;
        private NpgsqlConnection? _connection;

        public DatabaseConnectionManager(string connectionString)
        {
            _connectionString = connectionString;
        }

        public async Task<NpgsqlConnection> GetConnectionAsync()
        {
            if (_connection == null || _connection.State != ConnectionState.Open)
            {
                _connection = new NpgsqlConnection(_connectionString);
                await _connection.OpenAsync();
            }

            return _connection;
        }

        public async Task CloseConnectionAsync()
        {
            if (_connection != null && _connection.State == ConnectionState.Open)
            {
                await _connection.CloseAsync();
            }
        }
    }
}