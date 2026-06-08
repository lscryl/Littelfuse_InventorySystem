using MySqlConnector;
// MySQL connector library
using Microsoft.Extensions.Configuration; // For reading appsettings.json

namespace ITInventorySystem.Data             // Replace 'YourProject' with your actual project name
{
    public class DbHelper
    {
        // Holds the connection string read from appsettings.json
        private readonly string _connectionString;

        // -------------------------------------------------------
        // Constructor: ASP.NET Core automatically injects
        // IConfiguration so we can read appsettings.json values.
        // -------------------------------------------------------
        public DbHelper(IConfiguration configuration)
        {
            // Read the connection string named "DefaultConnection"
            // from appsettings.json
            _connectionString = configuration
                .GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' not found in appsettings.json");
        }

        // -------------------------------------------------------
        // GetConnection()
        // Returns an open MySqlConnection.
        // Usage in Controllers:
        //   using var conn = _db.GetConnection();
        //   (No need to manually close — 'using' handles it)
        // -------------------------------------------------------
        public MySqlConnection GetConnection()
        {
            // Create a new MySQL connection with our connection string
            var connection = new MySqlConnection(_connectionString);

            // Open the connection (equivalent to mysqli connect in PHP)
            connection.Open();

            // Return the open connection to the caller
            return connection;
        }

        // -------------------------------------------------------
        // TestConnection()
        // Returns true if DB connection succeeds, false otherwise.
        // Useful for health checks or startup validation.
        // Mirrors PHP's: if ($conn->connect_error) { die(...) }
        // -------------------------------------------------------
        public bool TestConnection(out string errorMessage)
        {
            errorMessage = string.Empty;

            try
            {
                // Try to open a connection
                using var connection = new MySqlConnection(_connectionString);
                connection.Open();

                // If we reach here, connection was successful
                return true;
            }
            catch (MySqlException ex)
            {
                // Capture the error message (like $conn->connect_error in PHP)
                errorMessage = $"Connection failed: {ex.Message}";
                return false;
            }
        }
    }
}