﻿using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Reflection;
using System.ServiceModel.Syndication;
using System.Xml;
using ErikEJ.SqlCeScripting;
using Microsoft.VisualStudio.Data.Core;
using Microsoft.VisualStudio.Data.Services;
#if SSMS
using Microsoft.Data.ConnectionUI;
using ErikEJ.SqlCeToolbox.SSMSEngine;
#else
using ErikEJ.SqlCeToolbox.Dialogs;
#endif
using ErikEJ.SQLiteScripting;
using System.Data.SqlClient;
using System.Data.SQLite;
using System.Diagnostics;
using Microsoft.VisualStudio.Shell;

namespace ErikEJ.SqlCeToolbox.Helpers
{

    internal class DataConnectionHelper
    {
        private static string separator = Environment.NewLine + "GO" + Environment.NewLine;

        internal static Dictionary<string, DatabaseInfo> GetDataConnections(SqlCeToolboxPackage package,
            bool includeServerConnections, bool serverConnectionsOnly)
        {
            // http://www.mztools.com/articles/2007/MZ2007018.aspx
            Dictionary<string, DatabaseInfo> databaseList = new Dictionary<string, DatabaseInfo>();
            var dataExplorerConnectionManager =
                package.GetServiceHelper(typeof(IVsDataExplorerConnectionManager)) as IVsDataExplorerConnectionManager;

            //Test code...

            //var objIVsDataProviderManager = package.GetServiceHelper(typeof(IVsDataProviderManager)) as IVsDataProviderManager;
            //var objIVsDataConnectionManager = package.GetServiceHelper(typeof(IVsDataConnectionManager)) as IVsDataConnectionManager;
            //var objIVsDataSourceManager = package.GetServiceHelper(typeof(IVsDataSourceManager)) as IVsDataSourceManager;

            //IVsDataProvider objIVsDataProvider;
            //foreach (var objIVsDataSource in objIVsDataSourceManager.Sources)
            //{
            //    //System.Diagnostics.Debug.WriteLine(objIVsDataSource.DisplayName);
            //    //System.Diagnostics.Debug.WriteLine(objIVsDataSource.Guid.ToString());

            //    //foreach (var objProviderGuid in objIVsDataSource.GetProviders())
            //    //{
            //    //    objIVsDataProvider = objIVsDataProviderManager.GetDataProvider(objProviderGuid);
            //    //    System.Diagnostics.Debug.WriteLine(objIVsDataProvider.DisplayName);
            //    //    System.Diagnostics.Debug.WriteLine(objIVsDataProvider.Guid.ToString());
            //    //}
            //}

            // End test code

            Guid provider35 = new Guid(Resources.SqlCompact35Provider);
            Guid provider40 = new Guid(Resources.SqlCompact40Provider);
            Guid providerSqLite = new Guid(Resources.SQLiteProvider);

            bool isV35Installed = IsV35Installed() && DdexProviderIsInstalled(provider35);
            bool isV40Installed = IsV40Installed() && DdexProviderIsInstalled(provider40);
            if (dataExplorerConnectionManager != null)
            {
                foreach (var connection in dataExplorerConnectionManager.Connections.Values)
                {
                    try
                    {
                        var objProviderGuid = connection.Provider;
                        if (!serverConnectionsOnly)
                        {
                            if ((objProviderGuid == provider35 && isV35Installed) ||
                                (objProviderGuid == provider40 && isV40Installed))
                            {
                                DatabaseType dbType = DatabaseType.SQLCE40;
                                if (objProviderGuid == provider35)
                                    dbType = DatabaseType.SQLCE35;
                                var serverVersion = "4.0";
                                if (dbType == DatabaseType.SQLCE35)
                                    serverVersion = "3.5";

                                var sConnectionString =
                                    DataProtection.DecryptString(connection.EncryptedConnectionString);
                                if (!sConnectionString.Contains("Mobile Device"))
                                {
                                    DatabaseInfo info = new DatabaseInfo();
                                    info.Caption = connection.DisplayName;
                                    info.FromServerExplorer = true;
                                    info.DatabaseType = dbType;
                                    info.ServerVersion = serverVersion;
                                    info.ConnectionString = sConnectionString;
                                    info.FileIsMissing = IsMissing(info);
                                    if (!databaseList.ContainsKey(sConnectionString))
                                        databaseList.Add(sConnectionString, info);
                                }
                            }

                            if (objProviderGuid == providerSqLite)
                            {
                                DatabaseType dbType = DatabaseType.SQLite;

                                var sConnectionString =
                                    DataProtection.DecryptString(connection.EncryptedConnectionString);
                                DatabaseInfo info = new DatabaseInfo();
                                info.Caption = connection.DisplayName;
                                info.FromServerExplorer = true;
                                info.DatabaseType = dbType;
                                //TODO Update this when SQLite provider is updated!
                                info.ServerVersion = "3.15";
                                info.ConnectionString = sConnectionString;
                                info.FileIsMissing = IsMissing(info);
                                if (!databaseList.ContainsKey(sConnectionString))
                                    databaseList.Add(sConnectionString, info);
                            }
                        }
                        if (includeServerConnections && objProviderGuid == new Guid(Resources.SqlServerDotNetProvider))
                        {
                            var sConnectionString = DataProtection.DecryptString(connection.EncryptedConnectionString);
                            var info = new DatabaseInfo();
                            info.Caption = connection.DisplayName;
                            info.FromServerExplorer = true;
                            info.DatabaseType = DatabaseType.SQLServer;
                            info.ServerVersion = string.Empty;
                            info.ConnectionString = sConnectionString;
                            if (!databaseList.ContainsKey(sConnectionString))
                                databaseList.Add(sConnectionString, info);
                        }
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                    catch (NullReferenceException)
                    {
                    }
                }
            }
#if SSMS
            try
            {
                var objectExplorerManager = new ObjectExplorerManager(package);
                var dte = package.GetServiceHelper(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
                if (dte.RegistryRoot.Contains("13.0"))
                {
                    var list = objectExplorerManager.GetAllServerUserDatabases("13");
                    foreach (var item in list)
                    {
                        if (!databaseList.ContainsKey(item.Key))
                            databaseList.Add(item.Key, item.Value);
                    }
                }
            }
            //TODO Make SSMS 14.x (17) work with above method!
            catch (MissingMethodException)
            {
            }
#endif
            return databaseList;
        }

        private static DatabaseType GetPreferredDatabaseType()
        {
            //Assume 3.5 is installed
            DatabaseType dbType = DatabaseType.SQLCE35;
            if (!IsV35Installed()) //So 4.0 is installed
            {
                if (!IsV40Installed())
                {
                    dbType = DatabaseType.SQLite;
                }
                else
                {
                    dbType = DatabaseType.SQLCE40;
                }
            }
            return dbType;
        }

        internal static Dictionary<string, DatabaseInfo> GetOwnDataConnections()
        {
            var databaseList = new Dictionary<string, DatabaseInfo>();
            var dbType = GetPreferredDatabaseType();
            var dbInfo = new DatabaseInfo {ConnectionString = CreateStore(dbType), DatabaseType = dbType};
            using (var repository = CreateRepository(dbInfo))
            {
                var script = "SELECT FileName, Source, CeVersion FROM Databases" + separator;
                var dataset = repository.ExecuteSql(script);
                foreach (DataRow row in dataset.Tables[0].Rows)
                {
                    var foundType = (DatabaseType) int.Parse(row[2].ToString());
                    if (!IsV35Installed() && foundType == DatabaseType.SQLCE35)
                    {
                        continue;
                    }
                    if (!IsV40Installed() && foundType == DatabaseType.SQLCE40)
                    {
                        continue;
                    }
                    var info = new DatabaseInfo();
                    try
                    {
                        info.Caption = Path.GetFileName(row[0].ToString());
                    }
                    catch (ArgumentException)
                    {
                        info.Caption = row[0].ToString();
                    }
                    var key = row[1].ToString();
                    info.DatabaseType = foundType;
                    info.FromServerExplorer = false;
                    info.ConnectionString = key;
                    info.ServerVersion = "4.0.0.0";
                    if (foundType == DatabaseType.SQLCE35)
                        info.ServerVersion = "3.5.1.0";
                    if (foundType == DatabaseType.SQLite)
                        //TODO Update this when SQLite provider is updated!
                        info.ServerVersion = "3.15";
                    info.FileIsMissing = IsMissing(info);
                    if (!databaseList.ContainsKey(key) && !info.FileIsMissing)
                    {
                        databaseList.Add(key, info);
                    }
                }
            }
            return databaseList;
        }

        internal static bool DdexProviderIsInstalled(Guid id)
        {
            IVsDataProvider provider;
            var objIVsDataProviderManager =
                Package.GetGlobalService(typeof(IVsDataProviderManager)) as IVsDataProviderManager;
            return objIVsDataProviderManager != null &&
                   objIVsDataProviderManager.Providers.TryGetValue(id, out provider);
        }

        internal void ValidateConnections(SqlCeToolboxPackage package)
        {
            var dataExplorerConnectionManager =
                package.GetServiceHelper(typeof(IVsDataExplorerConnectionManager)) as IVsDataExplorerConnectionManager;
            var removals = new List<IVsDataExplorerConnection>();

            if (dataExplorerConnectionManager != null)
            {
                foreach (var connection in dataExplorerConnectionManager.Connections.Values)
                {
                    try
                    {
                        var objProviderGuid = connection.Provider;
                        if ((objProviderGuid == new Guid(Resources.SqlCompact35Provider) && IsV35Installed()) ||
                            (objProviderGuid == new Guid(Resources.SqlCompact40Provider) && IsV40Installed()))
                        {
                            connection.Connection.Open();
                            connection.Connection.Close();
                        }
                    }
                    catch (Exception ex)
                    {
                        if (ex.GetType().Name == "SqlCeException")
                        {
                            removals.Add(connection);
                        }
                        if (ex.GetType().Name == "SqlCeInvalidDatabaseFormatException")
                        {
                            removals.Add(connection);
                        }
                        if (ex.GetType() == typeof(ArgumentException))
                        {
                            removals.Add(connection);
                        }
                        if (ex.GetType() == typeof(KeyNotFoundException))
                        {
                            removals.Add(connection);
                        }
                        throw;
                    }

                }
                for (int i = removals.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        dataExplorerConnectionManager.RemoveConnection(removals[i]);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (IndexOutOfRangeException)
                    {
                    }
                    catch (KeyNotFoundException)
                    {
                    }
                }
            }

            var ownConnections = GetOwnDataConnections();
            foreach (var item in ownConnections)
            {
                try
                {
                    using (CreateRepository(item.Value))
                    {
                    }
                }
                catch (Exception ex)
                {
                    if (ex.GetType().Name == "SqlCeException"
                        || ex.GetType().Name == "SqlCeInvalidDatabaseFormatException")
                    {
#if DEBUG
                        Debug.WriteLine(ex.Message);
#endif
                        RemoveDataConnection(item.Value.ConnectionString);
                    }
                    throw;
                }
            }
        }

        internal void ScanConnections(SqlCeToolboxPackage package)
        {
            var dte = package.GetServiceHelper(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
            var helper = CreateEngineHelper(DatabaseType.SQLCE40);
            EnvDteHelper dteHelper = new EnvDteHelper();
            var list = dteHelper.GetSqlCeFilesInActiveSolution(dte);
            foreach (var path in list)
            {
                if (File.Exists(path))
                {
                    bool versionFound = false;
                    SQLCEVersion version = SQLCEVersion.SQLCE20;
                    try
                    {
                        version = helper.DetermineVersion(path);
                        versionFound = true;
                    }
                    catch
                    {
                        // ignored
                    }
                    string connectionString = string.Format("Data Source={0}", path);
                    if (versionFound)
                    {
                        if (version == SQLCEVersion.SQLCE35)
                        {
                            SaveDataConnection(connectionString, DatabaseType.SQLCE35, package);
                        }
                        else if (version == SQLCEVersion.SQLCE40)
                        {
                            SaveDataConnection(connectionString, DatabaseType.SQLCE40, package);
                        }
                    }
                    else
                    {
                        var dbInfo = new DatabaseInfo();
                        dbInfo.DatabaseType = DatabaseType.SQLite;
                        dbInfo.ConnectionString = connectionString;
                        try
                        {
                            using (var repo = CreateRepository(dbInfo))
                            {
                                repo.GetAllTableNames();
                            }
                            SaveDataConnection(connectionString, DatabaseType.SQLite, package);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }
        }

        internal static void SaveDataConnection(SqlCeToolboxPackage package, string encryptedConnectionString,
            DatabaseType dbType, Guid provider)
        {
            var dataExplorerConnectionManager =
                package.GetServiceHelper(typeof(IVsDataExplorerConnectionManager)) as IVsDataExplorerConnectionManager;
            if (dataExplorerConnectionManager != null)
            {
                var savedName = GetFileName(DataProtection.DecryptString(encryptedConnectionString), dbType);
                dataExplorerConnectionManager.AddConnection(savedName, provider, encryptedConnectionString, true);
            }
        }

        public static string GetFilePath(string connectionString, DatabaseType dbType)
        {
            var helper = CreateEngineHelper(dbType);
            return helper.PathFromConnectionString(connectionString);
        }

        private static string GetFileName(string connectionString, DatabaseType dbType)
        {
            if (dbType == DatabaseType.SQLServer)
            {
                var helper = new SqlServerHelper();
                return helper.PathFromConnectionString(connectionString);
            }
            var filePath = GetFilePath(connectionString, dbType);
            return Path.GetFileName(filePath);
        }

        internal static void SaveDataConnection(string connectionString, DatabaseType dbType,
            SqlCeToolboxPackage package)
        {
            var storeDbType = GetPreferredDatabaseType();
            var helper = CreateEngineHelper(storeDbType);
            string path = CreateEngineHelper(dbType).PathFromConnectionString(connectionString);
            helper.SaveDataConnection(CreateStore(storeDbType), connectionString, path, dbType.GetHashCode());

            if (package.VsSupportsSimpleDdex4Provider() && dbType == DatabaseType.SQLCE40)
            {
                SaveDataConnection(package, DataProtection.EncryptString(connectionString), dbType,
                    new Guid(Resources.SqlCompact40PrivateProvider));
            }
        }

        internal static void RemoveDataConnection(string connectionString)
        {
            var storeType = GetPreferredDatabaseType();
            var helper = CreateEngineHelper(storeType);
            helper.DeleteDataConnnection(CreateStore(storeType), connectionString);
        }

        internal static void RenameDataConnection(string connectionString, string description)
        {
            var storeType = GetPreferredDatabaseType();
            var helper = CreateEngineHelper(storeType);
            helper.UpdateDataConnection(CreateStore(storeType), connectionString, description);
        }

        internal static void RemoveDataConnection(SqlCeToolboxPackage package, string connectionString, Guid provider)
        {
            var removals = new List<IVsDataExplorerConnection>();
            var dataExplorerConnectionManager =
                package.GetServiceHelper(typeof(IVsDataExplorerConnectionManager)) as IVsDataExplorerConnectionManager;
            if (dataExplorerConnectionManager != null)
            {
                foreach (var connection in dataExplorerConnectionManager.Connections.Values)
                {
                    var objProviderGuid = connection.Provider;
                    if ((objProviderGuid == new Guid(Resources.SqlCompact35Provider)) ||
                        (objProviderGuid == new Guid(Resources.SqlCompact40Provider)))
                    {
                        if (DataProtection.DecryptString(connection.EncryptedConnectionString) == connectionString)
                        {
                            removals.Add(connection);
                        }
                    }
                }

                for (int i = removals.Count - 1; i >= 0; i--)
                {
                    try
                    {
                        dataExplorerConnectionManager.RemoveConnection(removals[i]);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (IndexOutOfRangeException)
                    {
                    }
                }
            }
        }

        public static string PromptForConnectionString(SqlCeToolboxPackage package)
        {
#if SSMS
            DataSource sqlDataSource = new DataSource("MicrosoftSqlServer", "Microsoft SQL Server");
            sqlDataSource.Providers.Add(DataProvider.SqlDataProvider);
            DataConnectionDialog dcd = new DataConnectionDialog();
            dcd.DataSources.Add(sqlDataSource);
            dcd.SelectedDataProvider = DataProvider.SqlDataProvider;
            dcd.SelectedDataSource = sqlDataSource;
            if (DataConnectionDialog.Show(dcd) == System.Windows.Forms.DialogResult.OK)
            {
                return dcd.ConnectionString;
            }
#else
            var databaseList = GetDataConnections(package, true, true);
            PickServerDatabaseDialog psd = new PickServerDatabaseDialog(databaseList);
            bool? res = psd.ShowModal();
            if (res.HasValue && res.Value && (psd.SelectedDatabase.Value != null))
            {
                return psd.SelectedDatabase.Value.ConnectionString;
            }

#endif
            return null;
        }

        private static string CreateStore(DatabaseType storeDbType)
        {
            string fileName = GetStoreName(storeDbType);
            string connString = string.Format("Data Source={0};", fileName);
            if (!File.Exists(fileName))
            {
                if (storeDbType == DatabaseType.SQLite)
                {
                    var helper = CreateEngineHelper(storeDbType);
                    helper.CreateDatabase(connString);
                }
                else
                {
                    var sdf = Resources.SqlCe35AddinStore;
                    if (storeDbType == DatabaseType.SQLCE40)
                        sdf = Resources.SqlCe40AddinStore;
                    using (Stream stream = new MemoryStream(sdf))
                    {
                        // Create a FileStream object to write a stream to a file 
                        using (FileStream fileStream = File.Create(fileName, (int) stream.Length))
                        {
                            // Fill the bytes[] array with the stream data 
                            byte[] bytesInStream = new byte[stream.Length];
                            stream.Read(bytesInStream, 0, bytesInStream.Length);
                            // Use FileStream object to write to the specified file 
                            fileStream.Write(bytesInStream, 0, bytesInStream.Length);
                        }
                    }
                }
            }

            var dbInfo = new DatabaseInfo {DatabaseType = storeDbType, ConnectionString = connString};
            using (IRepository repository = CreateRepository(dbInfo))
            {
                var tables = repository.GetAllTableNames();
                if (!tables.Contains("Databases"))
                {
                    var script =
                        "CREATE TABLE Databases (Id INT IDENTITY, Source nvarchar(2048) NOT NULL, FileName nvarchar(512) NOT NULL, CeVersion int NOT NULL)" +
                        separator;
                    if (storeDbType == DatabaseType.SQLite)
                        script =
                            "CREATE TABLE Databases (Id INTEGER PRIMARY KEY, Source nvarchar(2048) NOT NULL, FileName nvarchar(512) NOT NULL, CeVersion int NOT NULL)" +
                            separator;
                    repository.ExecuteSql(script);
                }
            }
            return connString;
        }

        private static string GetStoreName(DatabaseType storeDbType)
        {
            string file = "SqlCe35AddinStore.sdf";
            if (storeDbType == DatabaseType.SQLCE40)
                file = "SqlCe40AddinStore.sdf";
            if (storeDbType == DatabaseType.SQLite)
            {
                file = "SQLiteAddinStore.db";
            }
            string fileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                file);
            return fileName;
        }

        public static IGenerator CreateGenerator(IRepository repository, string outFile, DatabaseType databaseType)
        {
            switch (databaseType)
            {
                case DatabaseType.SQLServer:
                    return new Generator(repository, outFile, false, Properties.Settings.Default.PreserveSqlDates, false,
                        Properties.Settings.Default.KeepSchemaNames);
                case DatabaseType.SQLCE35:
                    return string.IsNullOrEmpty(outFile)
                        ? new Generator(repository)
                        : new Generator(repository, outFile);
                case DatabaseType.SQLCE40:
                    return string.IsNullOrEmpty(outFile)
                        ? new Generator4(repository)
                        : new Generator4(repository, outFile);
                case DatabaseType.SQLite:
                    return new Generator(repository, outFile, false, false, true);
                default:
                    return null;
            }
        }

        public static ISqlCeHelper CreateEngineHelper(DatabaseType databaseType)
        {
            switch (databaseType)
            {
                case DatabaseType.SQLCE35:
                    return new SqlCeHelper();
                case DatabaseType.SQLCE40:
                    return new SqlCeHelper4();
                case DatabaseType.SQLServer:
                case DatabaseType.SQLite:
                    return new SqliteHelper();
                default:
                    return null;
            }
        }

        public static IGenerator CreateGenerator(IRepository repository, DatabaseType databaseType)
        {
            return CreateGenerator(repository, null, databaseType);
        }

        public static IRepository CreateRepository(DatabaseInfo databaseInfo)
        {
            switch (databaseInfo.DatabaseType)
            {
                case DatabaseType.SQLCE35:
                    return new DBRepository(databaseInfo.ConnectionString);
                case DatabaseType.SQLCE40:
                    return new DB4Repository(databaseInfo.ConnectionString);
                case DatabaseType.SQLServer:
                    return new ServerDBRepository(databaseInfo.ConnectionString,
                        Properties.Settings.Default.KeepSchemaNames);
                case DatabaseType.SQLite:
                    return new SQLiteRepository(databaseInfo.ConnectionString);
                default:
                    return null;
            }
        }

        public static void RegisterDdexProviders()
        {
            //TODO Consider removing this
            try
            {
                var ver = SqlCeToolboxPackage.VisualStudioVersion.ToString(1);

                if (SqlCeToolboxPackage.VisualStudioVersion >= new Version(12, 0))
                {
                    DdexRegistry.AddDdex4Registrations(ver);
                }
            }
            catch (Exception ex)
            {
                SendError(ex, DatabaseType.SQLServer);
            }
        }

        public static bool CheckVersion(string lookingFor)
        {
            try
            {
                using (var wc = new System.Net.WebClient())
                {
                    wc.Proxy = System.Net.WebRequest.GetSystemWebProxy();
                    var xDoc = new XmlDocument();
                    var s = wc.DownloadString(@"http://www.sqlcompact.dk/SqlCeToolboxVersions.xml");
                    xDoc.LoadXml(s);

                    if (xDoc.DocumentElement != null)
                    {
                        var newVersion = xDoc.DocumentElement.Attributes[lookingFor].Value;

                        var vN = new Version(newVersion);
                        if (vN > Assembly.GetExecutingAssembly().GetName().Version)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // ignored
            }
            return false;
        }

        public static string GetDownloadCount()
        {
            try
            {
                var reader =
                    XmlReader.Create(
                        "http://sqlcompact.dk/vsgallerycounter/downloadfeed.axd?extensionId=0e313dfd-be80-4afb-b5e9-6e74d369f7a1");
                var feed = SyndicationFeed.Load(reader);
                if (feed != null)
                    foreach (var item in feed.Items)
                    {
                        return string.Format("- {0:0,0} downloads", double.Parse(item.Summary.Text));
                    }
            }
            catch
            {
                // ignored
            }
            return string.Format("- more than {0:0,0} downloads", 610000d);
        }

        public static string GetSqlCeFileFilter()
        {
            return string.Format("SQL Server Compact Database|{0}|All Files|*.*",
                Properties.Settings.Default.FileFilterSqlCe);
        }

        public static string GetSqliteFileFilter()
        {
            return string.Format("SQLite Database file|{0}|All Files|*.*", Properties.Settings.Default.FileFilterSqlite);
        }

        internal static void LogUsage(string feature)
        {
            Telemetry.TrackEvent(feature);
        }

        internal static string SendError(Exception ex, DatabaseType dbType, bool report = true)
        {
            if (ex != null)
            {
                var dontTrack = ex.GetType().Name == "SqlCeException"
                                || ex.GetType().Name == "SqlCeInvalidDatabaseFormatException"
                                || ex is SqlException
                                || ex is DBConcurrencyException
                                || ex is SQLiteException;

                if (!dontTrack && report)
                {
                    Telemetry.TrackException(ex);
                }
                EnvDteHelper.ShowError(CreateEngineHelper(dbType).FormatError(ex));
            }
            return string.Empty;
        }

        internal static bool IsMissing(DatabaseInfo info)
        {
            try
            {
                var path = GetFilePath(info.ConnectionString, info.DatabaseType);
                return !File.Exists(path);
            }
            catch
            {
                return false;
            }
        }

        internal static bool IsV40Installed()
        {
            return new SqlCeHelper4().IsV40Installed() != null;
        }

        internal static bool IsV35Installed()
        {
            return new SqlCeHelper().IsV35Installed() != null;
        }

        internal static bool IsV40DbProviderInstalled()
        {
            return new SqlCeHelper4().IsV40DbProviderInstalled();
        }

        internal static bool IsSqLiteDbProviderInstalled()
        {
            try
            {
                System.Data.Common.DbProviderFactories.GetFactory("System.Data.SQLite.EF6");
            }
            catch 
            {
                return false;
            }
            return true;
        }

        internal static bool IsV35DbProviderInstalled()
        {
            return new SqlCeHelper4().IsV35DbProviderInstalled();
        }

        internal static bool IsSyncFx21Installed()
        {
            try
            {
                Assembly.Load("Microsoft.Synchronization.Data.SqlServerCe, Version=3.1.0.0, Culture=neutral, PublicKeyToken=89845dcd8080cc91");
            }
            catch
            {
                return false;
            }
            return true;
        }

        internal static bool IsMsReportViewer10Installed()
        {
            try
            {
                Assembly.Load("Microsoft.ReportViewer.WinForms, Version=10.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a");
            }
            catch
            {
                return false;
            }
            return true;
        }
    }
}
