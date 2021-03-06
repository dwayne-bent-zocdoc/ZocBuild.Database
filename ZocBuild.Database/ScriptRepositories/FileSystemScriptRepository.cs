﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZocBuild.Database.Errors;
using ZocBuild.Database.Exceptions;
using ZocBuild.Database.SqlParser;
using ZocBuild.Database.Util;

namespace ZocBuild.Database.ScriptRepositories
{
    /// <summary>
    /// Represents a script repository that stores build scripts at a location on disk.
    /// </summary>
    /// <remarks>
    /// This repository requires a specific directory structure.  The root location must contain 
    /// subdirectories for each database schema, each named with the name of the schema.  Within 
    /// those directories should be a directory for each database object type: Function, Procedure, 
    /// Type, View.  Within those directories should be the build scripts, with file names ending 
    /// with ".sql".  The name of each file should be the same name as the database object it 
    /// creates.
    /// </remarks>
    public class FileSystemScriptRepository : IScriptRepository
    {
        private readonly Dictionary<string, DatabaseObjectType> objectTypes;
        private readonly IParser sqlParser;

        #region Constructors

        /// <summary>
        /// Instantiates a file system repository for the given database at the specified directory location.
        /// </summary>
        /// <param name="scriptDirectoryPath">The path to the directory where build scripts are located.</param>
        /// <param name="serverName">The name of the database server.</param>
        /// <param name="databaseName">The name of the database.</param>
        /// <param name="sqlParser">The sql script parser for reading the SQL file contents.</param>
        public FileSystemScriptRepository(string scriptDirectoryPath, string serverName, string databaseName, IParser sqlParser)
            : this(new DirectoryInfo(scriptDirectoryPath), serverName, databaseName, sqlParser)
        {
        }

        /// <summary>
        /// Instantiates a file system repository for the given database at the specified directory location.
        /// </summary>
        /// <param name="scriptDirectory">The directory where build scripts are located.</param>
        /// <param name="serverName">The name of the database server.</param>
        /// <param name="databaseName">The name of the database.</param>
        /// <param name="sqlParser">The sql script parser for reading the SQL file contents.</param>
        public FileSystemScriptRepository(DirectoryInfo scriptDirectory, string serverName, string databaseName, IParser sqlParser)
        {
            ScriptDirectory = scriptDirectory;
            ServerName = serverName.TrimObjectName();
            DatabaseName = databaseName.TrimObjectName();
            objectTypes = Enum.GetValues(typeof(DatabaseObjectType)).Cast<DatabaseObjectType>()
                .ToDictionary(x => x.ToString(), y => y, StringComparer.InvariantCultureIgnoreCase);
            this.sqlParser = sqlParser;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the directory where build scripts are located.
        /// </summary>
        public DirectoryInfo ScriptDirectory { get; private set; }

        /// <summary>
        /// Gets the name of the database server.
        /// </summary>
        public string ServerName { get; private set; }

        /// <summary>
        /// Gets the name of the database.
        /// </summary>
        public string DatabaseName { get; private set; }

        #endregion

        #region IScriptRepository Members

        /// <summary>
        /// This method is not supported and calling it will raise a <see cref="NotSupportedException"/>.
        /// </summary>
        /// <returns>Nothing; an exception will be raised.</returns>
        public virtual Task<IEnumerable<ScriptFile>> GetChangedScriptsAsync()
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Gets all the build scripts for database objects contained in the repository.
        /// </summary>
        /// <returns>A collection of build scripts.</returns>
        public virtual async Task<IEnumerable<ScriptFile>> GetAllScriptsAsync()
        {
            return await GetScriptsAsync(f => true);
        }

        /// <summary>
        /// Retrieves the build script corresponding to the given database object identifier.
        /// </summary>
        /// <param name="dbObject">The database object for which a build script is desired.</param>
        /// <returns>A build script.</returns>
        public virtual async Task<ScriptFile> GetScriptAsync(TypedDatabaseObject dbObject)
        {
            if (!string.Equals(dbObject.ServerName, ServerName, StringComparison.InvariantCultureIgnoreCase)
                || !string.Equals(dbObject.DatabaseName, DatabaseName, StringComparison.InvariantCultureIgnoreCase))
            {
                return new ScriptFile(dbObject, new MissingScriptFileError(dbObject));
            }
            var file = new FileInfo(Path.Combine(ScriptDirectory.FullName, dbObject.SchemaName, dbObject.ObjectType.ToString(), dbObject.ObjectName + ".sql"));
            return await GetScriptAsync(file);
        }

        #endregion

        #region Helper methods

        /// <summary>
        /// Gets the build scripts in the repository that satisfy the given predicate.
        /// </summary>
        /// <param name="filter">The predicate with which the build scripts are filtered.</param>
        /// <returns>A collection of build scripts.</returns>
        protected virtual async Task<IEnumerable<ScriptFile>> GetScriptsAsync(Func<FileInfo, bool> filter)
        {
            var scripts = new List<ScriptFile>();
            foreach(var f in ScriptDirectory.GetFiles("*.sql", SearchOption.AllDirectories).Where(filter))
            {
                var script = await GetScriptAsync(f);
                scripts.Add(script);
            }
            return scripts;
        }

        /// <summary>
        /// Retrieves the build script contained in the given file.
        /// </summary>
        /// <param name="file">The file containing a build script</param>
        /// <returns>A build script.</returns>
        protected async Task<ScriptFile> GetScriptAsync(FileInfo file)
        {
            // Parse file name and path
            var typeName = file.Directory.Name;
            var schemaName = file.Directory.Parent.Name;
            var fileName = Path.GetFileNameWithoutExtension(file.FullName);
            if(!objectTypes.ContainsKey(typeName))
            {
                return new ScriptFile(new DatabaseObject(ServerName, DatabaseName, schemaName.TrimObjectName(), fileName.TrimObjectName()), new UnexpectedObjectTypeError(typeName));
            }
            var objectType = objectTypes[typeName];
            var dbObject = new TypedDatabaseObject(ServerName, DatabaseName, schemaName.TrimObjectName(), fileName.TrimObjectName(), objectType);

            // Read file contents
            string content;
            try
            {
                using (var fileInputStream = file.OpenRead())
                {
                    using (var fileReader = new StreamReader(fileInputStream))
                    {
                        content = await fileReader.ReadToEndAsync();
                    }
                }
            }
            catch (Exception)
            {
                return new ScriptFile(dbObject, GetDropScript(dbObject), sqlParser);
            }

            // Parse script file
            var script = new ScriptFile(dbObject, content, sqlParser);

            return script;
        }

        private static string GetDropScript(TypedDatabaseObject dbObject)
        {
            return string.Format("DROP {0} [{1}].[{2}]", dbObject.ObjectType, dbObject.SchemaName, dbObject.ObjectName);
        }

        #endregion
    }
}
