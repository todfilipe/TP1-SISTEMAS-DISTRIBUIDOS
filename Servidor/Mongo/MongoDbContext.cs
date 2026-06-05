using Microsoft.Extensions.Configuration;
using MongoDB.Driver;

namespace Servidor.Mongo;

public class MongoDbContext
{
    private const string DefaultDatabaseName = "urbanodb";

    private static readonly object ClientLock = new();
    private static MongoClient? sharedClient;
    private static string? sharedConnectionString;

    public MongoDbContext()
        : this(LoadConfiguration())
    {
    }

    public MongoDbContext(IConfiguration configuration)
    {
        ConnectionString = ResolveConnectionString(configuration);
        DatabaseName = ResolveDatabaseName(configuration);
        Client = GetOrCreateClient(ConnectionString);
        Database = Client.GetDatabase(DatabaseName);
    }

    public string ConnectionString { get; }

    public string DatabaseName { get; }

    public IMongoClient Client { get; }

    public IMongoDatabase Database { get; }

    private static IConfiguration LoadConfiguration()
    {
        return new ConfigurationBuilder()
            .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();
    }

    private static MongoClient GetOrCreateClient(string connectionString)
    {
        if (sharedClient != null && sharedConnectionString == connectionString)
        {
            return sharedClient;
        }

        lock (ClientLock)
        {
            if (sharedClient == null)
            {
                var settings = MongoClientSettings.FromConnectionString(connectionString);
                settings.ConnectTimeout = TimeSpan.FromSeconds(3);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(3);

                sharedConnectionString = connectionString;
                sharedClient = new MongoClient(settings);
            }
            else if (sharedConnectionString != connectionString)
            {
                throw new InvalidOperationException("MongoClient ja foi inicializado com outra connection string.");
            }

            return sharedClient;
        }
    }

    private static string ResolveConnectionString(IConfiguration configuration)
    {
        string? envConnectionString = Environment.GetEnvironmentVariable("MONGODB_URI");
        if (!string.IsNullOrWhiteSpace(envConnectionString))
        {
            return envConnectionString;
        }

        string? configured = configuration["MongoDb:ConnectionString"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        string username = configuration["MongoDb:Username"]
            ?? Environment.GetEnvironmentVariable("MONGODB_ROOT_USERNAME")
            ?? "admin";
        string password = configuration["MongoDb:Password"]
            ?? Environment.GetEnvironmentVariable("MONGODB_ROOT_PASSWORD")
            ?? "admin";
        string host = configuration["MongoDb:Host"]
            ?? Environment.GetEnvironmentVariable("MONGODB_HOST")
            ?? "localhost";
        string port = configuration["MongoDb:Port"]
            ?? Environment.GetEnvironmentVariable("MONGODB_PORT")
            ?? "27017";
        string authSource = configuration["MongoDb:AuthSource"]
            ?? Environment.GetEnvironmentVariable("MONGODB_AUTH_SOURCE")
            ?? "admin";
        string databaseName = ResolveDatabaseName(configuration);

        return $"mongodb://{Uri.EscapeDataString(username)}:{Uri.EscapeDataString(password)}@{host}:{port}/{databaseName}?authSource={Uri.EscapeDataString(authSource)}";
    }

    private static string ResolveDatabaseName(IConfiguration configuration)
    {
        return configuration["MongoDb:DatabaseName"]
            ?? Environment.GetEnvironmentVariable("MONGODB_DATABASE")
            ?? DefaultDatabaseName;
    }
}
