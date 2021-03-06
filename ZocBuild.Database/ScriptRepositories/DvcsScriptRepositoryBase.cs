﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using ZocBuild.Database.SqlParser;

namespace ZocBuild.Database.ScriptRepositories
{
    /// <summary>
    /// Represents a script repository that stores build scripts in a distributed version control 
    /// system.
    /// </summary>
    /// <remarks>
    /// The build script files in the distributed version repository must be organized with the 
    /// same structure as required by <see cref="FileSystemScriptRepository"/>.  The root location 
    /// must contain subdirectories for each database schema, each named with the name of the 
    /// schema.  Within those directories should be a directory for each database object type: 
    /// Function, Procedure, Type, View.  Within those directories should be the build scripts, 
    /// with file names ending with ".sql".  The name of each file should be the same name as the 
    /// database object it creates.
    /// </remarks>
    public abstract class DvcsScriptRepositoryBase : FileSystemScriptRepository
    {
        #region Constructors

        /// <summary>
        /// Instantiates a dvcs script repository for the given database at the specified directory location.
        /// </summary>
        /// <param name="scriptDirectoryPath">The path to the directory where build scripts are located.</param>
        /// <param name="serverName">The name of the database server.</param>
        /// <param name="databaseName">The name of the database.</param>
        /// <param name="sqlParser">The sql script parser for reading the SQL file contents.</param>
        protected DvcsScriptRepositoryBase(string scriptDirectoryPath, string serverName, string databaseName, IParser sqlParser)
            : base(scriptDirectoryPath, serverName, databaseName, sqlParser)
        {
        }

        /// <summary>
        /// Instantiates a dvcs script repository for the given database at the specified directory location.
        /// </summary>
        /// <param name="scriptDirectory">The directory where build scripts are located.</param>
        /// <param name="serverName">The name of the database server.</param>
        /// <param name="databaseName">The name of the database.</param>
        /// <param name="sqlParser">The sql script parser for reading the SQL file contents.</param>
        protected DvcsScriptRepositoryBase(DirectoryInfo scriptDirectory, string serverName, string databaseName, IParser sqlParser)
            : base(scriptDirectory, serverName, databaseName, sqlParser)
        {
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the revision against which changes should be compared.
        /// </summary>
        public RevisionIdentifierBase SourceChangeset { get; set; }

        #endregion

        #region IScriptRepository Members

        /// <summary>
        /// Gets the build scripts for database objects that have changed since a specified 
        /// repository state.
        /// </summary>
        /// <remarks>
        /// This method returns the scripts that have changed between the revision specified by 
        /// <see cref="SourceChangeset"/> and the current HEAD.
        /// </remarks>
        /// <returns>A collection of build scripts.</returns>
        public override async Task<IEnumerable<ScriptFile>> GetChangedScriptsAsync()
        {
            var files = await GetDiffedFilesAsync();
            var scripts = new List<ScriptFile>();
            foreach (var scriptFile in files)
            {
                var script = await GetScriptAsync(scriptFile);
                scripts.Add(script);
            }
            return scripts;
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Gets the files that have changed between the revision specified by <see cref="SourceChangeset"/> 
        /// and the current HEAD.
        /// </summary>
        /// <returns>A collection of files.</returns>
        protected abstract Task<ICollection<FileInfo>> GetDiffedFilesAsync();

        /// <summary>
        /// Awaits until the given process exits.
        /// </summary>
        /// <param name="p">The process to wait.</param>
        protected async Task WaitForProcessExitAsync(Process p)
        {
            // TODO: make async
            p.WaitForExit();
        }

        #endregion

        #region Revision Identifiers

        /// <summary>
        /// Represents an identifier of a revision in the DVCS history.
        /// </summary>
        public abstract class RevisionIdentifierBase
        {
        }

        /// <summary>
        /// Represents the identifier of a tag or branch.
        /// </summary>
        public sealed class Tag : RevisionIdentifierBase
        {
            /// <summary>
            /// Instantiates a tag/branch.
            /// </summary>
            /// <param name="tagName">The name of the tag or branch.</param>
            public Tag(string tagName)
            {
                TagName = tagName;
            }

            private Tag()
            {
            }

            /// <summary>
            /// Gets or sets the name of the tag or branch.
            /// </summary>
            public string TagName { get; set; }

            public override string ToString()
            {
                return TagName;
            }
        }

        /// <summary>
        /// Represents the identifier of a specific revision.
        /// </summary>
        public sealed class ChangesetId : RevisionIdentifierBase
        {
            /// <summary>
            /// Instantiates the changesetId with the given SHA hash.
            /// </summary>
            /// <param name="hash">A length 20 byte array containing the hash value.</param>
            public ChangesetId(byte[] hash)
            {
                if (hash.Length != 20)
                {
                    throw new ArgumentException("hash");
                }
                Hash = hash;
            }

            /// <summary>
            /// Instantiates the changesetId with the given SHA hash.
            /// </summary>
            /// <param name="hex">A length 40 string containing the base 16 encoding of the hash value.</param>
            public ChangesetId(string hex)
            {
                hex = hex.Trim();
                if (hex.Length != 40)
                {
                    throw new ArgumentException("hex");
                }
                Hash = new byte[20];
                for (int index = 0; index < this.Hash.Length; index++)
                {
                    Hash[index] = Convert.ToByte(hex.Substring(index * 2, 2), 16);
                }
            }

            private ChangesetId()
            {
                Hash = new byte[20];
            }

            /// <summary>
            /// Gets or sets the hash of the changeset.
            /// </summary>
            public byte[] Hash { get; set; }

            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                foreach (var b in Hash)
                {
                    sb.AppendFormat("{0:x2}", b);
                }
                return sb.ToString();
            }
        }

        #endregion

        protected static Process CreateProcess(FileInfo executable, string arguments, DirectoryInfo workingDirectory)
        {
            var p = new Process();
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.CreateNoWindow = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.WorkingDirectory = workingDirectory.FullName;
            p.StartInfo.FileName = executable.FullName;
            p.StartInfo.Arguments = arguments;
            return p;
        }
    }
}
