using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace Uno.Maui.Integrity
{
	// Never loads the inspected assembly. The private snapshot is also the input to hashing
	// and metadata inspection, so consumers need not reopen a mutable pathname.
	public sealed class ManagedImage : IDisposable
	{
		readonly byte[] bytes;
		IDisposable lease;
		public string Path { get; }
		public string Sha256 { get; }
		public string AssemblyName { get; }
		public Guid Mvid { get; }
		public int Length => bytes.Length;
		public bool IsReferenceAssembly { get; }

		ManagedImage(string path, IDisposable stream, byte[] image)
		{
			Path = path;
			lease = stream;
			bytes = image;
			using (var hash = SHA256.Create())
				Sha256 = BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
			using (var input = new MemoryStream(bytes, false))
			using (var pe = new PEReader(input))
			{
				if (!pe.HasMetadata || pe.PEHeaders.CorHeader == null || pe.PEHeaders.PEHeader == null)
					throw new BadImageFormatException("Managed PE metadata is required: " + path);
				// PEReader is lazy. In particular, valid metadata near the start does not
				// establish that the final section of a truncated file is present.
				if (pe.PEHeaders.PEHeader.SizeOfHeaders > bytes.Length)
					throw new BadImageFormatException("Truncated PE headers: " + path);
				// The Authenticode directory uses a file offset, not an RVA or section.
				var certificate = pe.PEHeaders.PEHeader.CertificateTableDirectory;
				if (certificate.RelativeVirtualAddress < 0 || certificate.Size < 0 ||
					(long)certificate.RelativeVirtualAddress + certificate.Size > bytes.Length)
					throw new BadImageFormatException("Truncated PE certificate: " + path);
				foreach (var section in pe.PEHeaders.SectionHeaders)
				{
					if (section.PointerToRawData < 0 || section.SizeOfRawData < 0 ||
						(long)section.PointerToRawData + section.SizeOfRawData > bytes.Length)
						throw new BadImageFormatException("Truncated PE section: " + path);
				}
				var metadata = pe.GetMetadataReader();
				if (!metadata.IsAssembly)
					throw new BadImageFormatException("An assembly, not a netmodule, is required: " + path);
				var assembly = metadata.GetAssemblyDefinition();
				AssemblyName = metadata.GetString(assembly.Name);
				Mvid = metadata.GetGuid(metadata.GetModuleDefinition().Mvid);
				if (String.IsNullOrEmpty(AssemblyName) || Mvid == Guid.Empty)
					throw new BadImageFormatException("Missing assembly identity or MVID: " + path);
				foreach (var handle in assembly.GetCustomAttributes())
				{
					var attribute = metadata.GetCustomAttribute(handle);
					if (attribute.Constructor.Kind != HandleKind.MemberReference)
						continue;
					var parent = metadata.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
					if (parent.Kind != HandleKind.TypeReference)
						continue;
					var type = metadata.GetTypeReference((TypeReferenceHandle)parent);
					if (metadata.GetString(type.Namespace) == "System.Runtime.CompilerServices" &&
						metadata.GetString(type.Name) == "ReferenceAssemblyAttribute")
						IsReferenceAssembly = true;
				}
				// Exercise type/signature/method-body data, not just the DOS/CLI headers.
				foreach (var handle in metadata.TypeDefinitions)
				{
					var type = metadata.GetTypeDefinition(handle);
					metadata.GetString(type.Name);
					metadata.GetString(type.Namespace);
				}
				foreach (var handle in metadata.MethodDefinitions)
				{
					var method = metadata.GetMethodDefinition(handle);
					metadata.GetString(method.Name);
					metadata.GetBlobBytes(method.Signature);
					if (method.RelativeVirtualAddress != 0)
						pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes();
				}
				foreach (var handle in metadata.AssemblyReferences)
					metadata.GetString(metadata.GetAssemblyReference(handle).Name);
				foreach (var handle in metadata.TypeReferences)
				{
					var type = metadata.GetTypeReference(handle);
					metadata.GetString(type.Name);
					metadata.GetString(type.Namespace);
				}
				foreach (var handle in metadata.FieldDefinitions)
				{
					var field = metadata.GetFieldDefinition(handle);
					metadata.GetString(field.Name);
					metadata.GetBlobBytes(field.Signature);
				}
			}
		}

		public static ManagedImage FromBytes(byte[] image, string expectedAssemblyName = null)
		{
			if (image == null)
				throw new ArgumentNullException(nameof(image));
			// The caller may reuse/mutate its buffer after validation.
			var result = new ManagedImage("<memory>", new MemoryStream(), (byte[])image.Clone());
			if (!String.IsNullOrEmpty(expectedAssemblyName) && result.AssemblyName != expectedAssemblyName)
			{
				result.Dispose();
				throw new BadImageFormatException("Unexpected assembly identity in snapshot.");
			}
			return result;
		}

		public static ManagedImage Open(string path, string expectedAssemblyName = null)
		{
			var fullPath = System.IO.Path.GetFullPath(path);
			// No delete/write sharing: keep the exact Windows file object protected through
			// consumption. On all hosts OpenRead below uses the immutable validated snapshot.
			var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
			try
			{
				if (stream.Length == 0 || stream.Length > Int32.MaxValue)
					throw new BadImageFormatException("Invalid managed image length: " + fullPath);
				var image = new byte[(int)stream.Length];
				int offset = 0;
				while (offset < image.Length)
				{
					int read = stream.Read(image, offset, image.Length - offset);
					if (read == 0)
						throw new EndOfStreamException("Managed image changed during snapshot: " + fullPath);
					offset += read;
				}
				if (stream.Length != image.Length)
					throw new IOException("Managed image changed during snapshot: " + fullPath);
				var result = new ManagedImage(fullPath, stream, image);
				if (!String.IsNullOrEmpty(expectedAssemblyName) && result.AssemblyName != expectedAssemblyName)
					throw new BadImageFormatException("Unexpected assembly identity: " + fullPath);
				return result;
			}
			catch
			{
				stream.Dispose();
				throw;
			}
		}

		public static void AssertReferenceCopy(ManagedImage reference, ManagedImage producerReference)
		{
			if (!reference.IsReferenceAssembly || !producerReference.IsReferenceAssembly)
				throw new BadImageFormatException("Both reference and producer refint must be reference assemblies.");
			// MVID equality alone is insufficient (CopyRefAssembly itself uses that shortcut).
			if (reference.Length != producerReference.Length || reference.Sha256 != producerReference.Sha256)
				throw new InvalidDataException("Reference publication differs from producer refint: " + reference.Path);
		}

		public Stream OpenRead()
		{
			if (lease == null)
				throw new ObjectDisposedException(nameof(ManagedImage));
			return new MemoryStream(bytes, 0, bytes.Length, false, false);
		}

		public void Dispose()
		{
			if (lease != null)
			{
				lease.Dispose();
				lease = null;
			}
		}
	}
}
