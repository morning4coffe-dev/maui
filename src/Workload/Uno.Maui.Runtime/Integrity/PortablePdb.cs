namespace Uno.Maui.Integrity
{
	// Portable-PDB metadata and its PE CodeView identity, not a full IL/debug-info verifier.
	public static class PortablePdb
	{
		public static bool HasPortableSymbols(System.IO.Stream implementation)
		{
			using (var pe = new System.Reflection.PortableExecutable.PEReader(
				implementation, System.Reflection.PortableExecutable.PEStreamOptions.LeaveOpen))
			{
				if (!pe.HasMetadata) return false;
				foreach (var entry in pe.ReadDebugDirectory())
					if (entry.Type == System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView && entry.IsPortableCodeView)
						return true;
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
				bool matched = false;
				foreach (var entry in pe.ReadDebugDirectory())
				{
					if (entry.Type != System.Reflection.PortableExecutable.DebugDirectoryEntryType.CodeView)
						continue;
					var codeView = pe.ReadCodeViewDebugDirectoryData(entry);
					if (entry.IsPortableCodeView && codeView.Guid == id.Guid && entry.Stamp == id.Stamp && codeView.Age == 1)
						matched = true;
				}
				if (!matched)
					throw new System.IO.InvalidDataException("Portable PDB does not match its implementation CodeView identity.");
				foreach (var handle in metadata.Documents)
					metadata.GetDocument(handle);
				foreach (var handle in metadata.MethodDebugInformation)
					metadata.GetMethodDebugInformation(handle);
			}
		}
	}
}
