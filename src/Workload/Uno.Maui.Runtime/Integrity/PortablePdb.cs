namespace Uno.Maui.Integrity
{
	// Portable-PDB metadata and its PE CodeView identity, not a full IL/debug-info verifier.
	public static class PortablePdb
	{
		public static bool RequiresExternalPortablePdb(System.IO.Stream implementation)
		{
			using (var pe = new System.Reflection.PortableExecutable.PEReader(
				implementation, System.Reflection.PortableExecutable.PEStreamOptions.LeaveOpen))
			{
				if (!pe.HasMetadata) return false;
				bool portableCodeView = false;
				int embeddedCount = 0;
				System.Reflection.PortableExecutable.DebugDirectoryEntry embedded = default(
					System.Reflection.PortableExecutable.DebugDirectoryEntry);
				foreach (var entry in pe.ReadDebugDirectory())
				{
					if (entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView && entry.IsPortableCodeView)
						portableCodeView = true;
					if (entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.EmbeddedPortablePdb)
					{
						embedded = entry;
						embeddedCount++;
					}
				}
				if (embeddedCount == 0)
					return portableCodeView;
				if (embeddedCount != 1)
					throw new System.IO.InvalidDataException("Implementation has multiple embedded portable PDB records.");

				System.Reflection.Metadata.MetadataReaderProvider provider;
				try
				{
					provider = pe.ReadEmbeddedPortablePdbDebugDirectoryData(embedded);
				}
				catch (System.BadImageFormatException error)
				{
					throw new System.IO.InvalidDataException("Embedded portable PDB is invalid.", error);
				}
				catch (System.IO.InvalidDataException error)
				{
					throw new System.IO.InvalidDataException("Embedded portable PDB is invalid.", error);
				}
				using (provider)
				{
					var metadata = provider.GetMetadataReader();
					var id = new System.Reflection.Metadata.BlobContentId(metadata.DebugMetadataHeader.Id);
					if (!MatchesPortableCodeView(pe, id))
						throw new System.IO.InvalidDataException("Embedded portable PDB does not match its implementation CodeView identity.");
					AssertReadable(metadata);
				}
				return false;
			}
		}

		public static void AssertMatches(System.IO.Stream symbols, System.IO.Stream implementation)
		{
			using (var provider = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(
				symbols, System.Reflection.Metadata.MetadataStreamOptions.LeaveOpen))
			using (var pe = new System.Reflection.PortableExecutable.PEReader(
				implementation, System.Reflection.PortableExecutable.PEStreamOptions.LeaveOpen))
			{
				var metadata = provider.GetMetadataReader();
				var id = new System.Reflection.Metadata.BlobContentId(metadata.DebugMetadataHeader.Id);
				if (!MatchesPortableCodeView(pe, id))
					throw new System.IO.InvalidDataException("Portable PDB does not match its implementation CodeView identity.");
				AssertReadable(metadata);
			}
		}

		public static string GetSourceLink(System.IO.Stream symbols)
		{
			using (var provider = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(
				symbols, System.Reflection.Metadata.MetadataStreamOptions.LeaveOpen))
			{
				var metadata = provider.GetMetadataReader();
				var sourceLinkKind = new System.Guid("CC110556-A091-4D38-9FEC-25AB9A351A6A");
				string sourceLink = null;
				int count = 0;
				foreach (var handle in metadata.CustomDebugInformation)
				{
					var information = metadata.GetCustomDebugInformation(handle);
					if (metadata.GetGuid(information.Kind) != sourceLinkKind)
						continue;
					if (information.Parent.Kind != System.Reflection.Metadata.HandleKind.ModuleDefinition)
						throw new System.IO.InvalidDataException("Portable PDB SourceLink record is not module-scoped.");
					sourceLink = System.Text.Encoding.UTF8.GetString(metadata.GetBlobBytes(information.Value));
					count++;
				}
				if (count != 1 || string.IsNullOrWhiteSpace(sourceLink))
					throw new System.IO.InvalidDataException("Portable PDB must contain one nonempty module SourceLink record.");
				return sourceLink;
			}
		}

		public static string[] GetDocumentNames(System.IO.Stream symbols)
		{
			using (var provider = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(
				symbols, System.Reflection.Metadata.MetadataStreamOptions.LeaveOpen))
			{
				var metadata = provider.GetMetadataReader();
				var names = new System.Collections.Generic.List<string>(metadata.Documents.Count);
				foreach (var handle in metadata.Documents)
					names.Add(metadata.GetString(metadata.GetDocument(handle).Name));
				return names.ToArray();
			}
		}

		public static string[] GetEmbeddedDocumentNames(System.IO.Stream symbols)
		{
			using (var provider = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(
				symbols, System.Reflection.Metadata.MetadataStreamOptions.LeaveOpen))
			{
				var metadata = provider.GetMetadataReader();
				var embeddedKind = new System.Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
				var rows = new System.Collections.Generic.HashSet<int>();
				foreach (var handle in metadata.CustomDebugInformation)
				{
					var information = metadata.GetCustomDebugInformation(handle);
					if (metadata.GetGuid(information.Kind) == embeddedKind)
					{
						if (information.Parent.Kind != System.Reflection.Metadata.HandleKind.Document)
							throw new System.IO.InvalidDataException("Embedded source record is not document-scoped.");
						rows.Add(System.Reflection.Metadata.Ecma335.MetadataTokens.GetRowNumber(information.Parent));
					}
				}
				var names = new System.Collections.Generic.List<string>(rows.Count);
				foreach (var handle in metadata.Documents)
				{
					var row = System.Reflection.Metadata.Ecma335.MetadataTokens.GetRowNumber(
						(System.Reflection.Metadata.EntityHandle)handle);
					if (rows.Contains(row))
						names.Add(metadata.GetString(metadata.GetDocument(handle).Name));
				}
				return names.ToArray();
			}
		}

		public static int[] ValidateSourceChecksums(System.IO.Stream symbols, string sourceRoot)
		{
			using (var provider = System.Reflection.Metadata.MetadataReaderProvider.FromPortablePdbStream(
				symbols, System.Reflection.Metadata.MetadataStreamOptions.LeaveOpen))
			{
				var metadata = provider.GetMetadataReader();
				var embeddedKind = new System.Guid("0E8A571B-6926-466E-B4AD-8AB04611F5FE");
				var embedded = new System.Collections.Generic.Dictionary<int, byte[]>();
				foreach (var handle in metadata.CustomDebugInformation)
				{
					var information = metadata.GetCustomDebugInformation(handle);
					if (metadata.GetGuid(information.Kind) == embeddedKind)
					{
						if (information.Parent.Kind != System.Reflection.Metadata.HandleKind.Document)
							throw new System.IO.InvalidDataException("Embedded source record is not document-scoped.");
						var row = System.Reflection.Metadata.Ecma335.MetadataTokens.GetRowNumber(information.Parent);
						if (embedded.ContainsKey(row))
							throw new System.IO.InvalidDataException("Portable PDB contains duplicate embedded source records.");
						embedded.Add(row, metadata.GetBlobBytes(information.Value));
					}
				}

				var root = string.IsNullOrWhiteSpace(sourceRoot) ? null :
					System.IO.Path.GetFullPath(sourceRoot).TrimEnd(
						System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) +
					System.IO.Path.DirectorySeparatorChar;
				var comparison = System.IO.Path.DirectorySeparatorChar == '\\'
					? System.StringComparison.OrdinalIgnoreCase
					: System.StringComparison.Ordinal;
				int validChecksums = 0;
				int embeddedMatches = 0;
				int physicalMatches = 0;
				int coveredDocuments = 0;
				foreach (var handle in metadata.Documents)
				{
					var document = metadata.GetDocument(handle);
					var algorithm = metadata.GetGuid(document.HashAlgorithm);
					var expected = metadata.GetBlobBytes(document.Hash);
					if (!IsSupportedHash(algorithm, expected.Length))
						throw new System.IO.InvalidDataException("Portable PDB document has an unsupported checksum.");
					validChecksums++;

					bool covered = false;
					var row = System.Reflection.Metadata.Ecma335.MetadataTokens.GetRowNumber(
						(System.Reflection.Metadata.EntityHandle)handle);
					byte[] embeddedBlob;
					if (embedded.TryGetValue(row, out embeddedBlob))
					{
						var bytes = DecodeEmbeddedSource(embeddedBlob);
						if (!HashesMatch(bytes, algorithm, expected))
							throw new System.IO.InvalidDataException("Embedded source does not match its PDB document checksum.");
						embeddedMatches++;
						covered = true;
					}

					var name = metadata.GetString(document.Name);
					if (root != null && System.IO.Path.IsPathRooted(name))
					{
						var path = System.IO.Path.GetFullPath(name);
						if (path.StartsWith(root, comparison) && System.IO.File.Exists(path))
						{
							if (!HashesMatch(System.IO.File.ReadAllBytes(path), algorithm, expected))
								throw new System.IO.InvalidDataException("Physical source does not match its PDB document checksum: " + name);
							physicalMatches++;
							covered = true;
						}
					}
					if (covered)
						coveredDocuments++;
				}
				return new[]
				{
					metadata.Documents.Count,
					validChecksums,
					embedded.Count,
					embeddedMatches,
					physicalMatches,
					coveredDocuments
				};
			}
		}

		static bool IsSupportedHash(System.Guid algorithm, int length)
		{
			return algorithm == new System.Guid("8829D00F-11B8-4213-878B-770E8597AC16") && length == 32 ||
				algorithm == new System.Guid("FF1816EC-AA5E-4D10-87F7-6F4963833460") && length == 20;
		}

		static bool HashesMatch(byte[] bytes, System.Guid algorithm, byte[] expected)
		{
			byte[] actual;
			if (algorithm == new System.Guid("8829D00F-11B8-4213-878B-770E8597AC16"))
			{
				using (var hash = System.Security.Cryptography.SHA256.Create())
					actual = hash.ComputeHash(bytes);
			}
			else
			{
				using (var hash = System.Security.Cryptography.SHA1.Create())
					actual = hash.ComputeHash(bytes);
			}
			if (actual.Length != expected.Length)
				return false;
			for (int i = 0; i < actual.Length; i++)
			{
				if (actual[i] != expected[i])
					return false;
			}
			return true;
		}

		static byte[] DecodeEmbeddedSource(byte[] blob)
		{
			if (blob.Length < 4)
				throw new System.IO.InvalidDataException("Embedded source record is truncated.");
			int length = System.BitConverter.ToInt32(blob, 0);
			if (length == 0)
			{
				var result = new byte[blob.Length - 4];
				System.Array.Copy(blob, 4, result, 0, result.Length);
				return result;
			}
			using (var input = new System.IO.MemoryStream(blob, 4, blob.Length - 4, false))
			using (var deflate = new System.IO.Compression.DeflateStream(
				input, System.IO.Compression.CompressionMode.Decompress))
			using (var output = new System.IO.MemoryStream())
			{
				deflate.CopyTo(output);
				var result = output.ToArray();
				if (result.Length != length)
					throw new System.IO.InvalidDataException("Embedded source length does not match its record.");
				return result;
			}
		}

		static bool MatchesPortableCodeView(
			System.Reflection.PortableExecutable.PEReader pe,
			System.Reflection.Metadata.BlobContentId id)
		{
			foreach (var entry in pe.ReadDebugDirectory())
			{
				if (entry.Type != System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView)
					continue;
				var codeView = pe.ReadCodeViewDebugDirectoryData(entry);
				if (entry.IsPortableCodeView && codeView.Guid == id.Guid && entry.Stamp == id.Stamp && codeView.Age == 1)
					return true;
			}
			return false;
		}

		static void AssertReadable(System.Reflection.Metadata.MetadataReader metadata)
		{
			foreach (var handle in metadata.Documents)
				metadata.GetDocument(handle);
			foreach (var handle in metadata.MethodDebugInformation)
				metadata.GetMethodDebugInformation(handle);
		}
	}
}
