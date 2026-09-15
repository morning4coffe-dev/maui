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
