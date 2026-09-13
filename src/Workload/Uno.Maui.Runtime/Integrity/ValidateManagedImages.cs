namespace Uno.Maui.Integrity
{
	public sealed class ValidateManagedImages : Microsoft.Build.Framework.ITask
	{
		public Microsoft.Build.Framework.IBuildEngine BuildEngine { get; set; }
		public Microsoft.Build.Framework.ITaskHost HostObject { get; set; }
		[Microsoft.Build.Framework.Required]
		public Microsoft.Build.Framework.ITaskItem[] Images { get; set; }
		[Microsoft.Build.Framework.Required]
		public string LeaseKey { get; set; }
		public Microsoft.Build.Framework.ITaskItem[] Symbols { get; set; }
		public Microsoft.Build.Framework.ITaskItem[] PackageFiles { get; set; }
		public bool VerifyOutputs { get; set; }
		public bool ExpectSymbols { get; set; }

		sealed class Symbol : System.IDisposable
		{
			public System.IO.FileStream File;
			public byte[] Bytes;
			public string Hash;
			public void Dispose() { File.Dispose(); }
		}

		sealed class Leases : System.IDisposable
		{
			public readonly System.Collections.Generic.List<ManagedImage> Images =
				new System.Collections.Generic.List<ManagedImage>();
			public readonly System.Collections.Generic.Dictionary<string, ManagedImage> PackageImages =
				new System.Collections.Generic.Dictionary<string, ManagedImage>(System.StringComparer.Ordinal);
			public readonly System.Collections.Generic.Dictionary<string, Symbol> Symbols =
				new System.Collections.Generic.Dictionary<string, Symbol>(System.StringComparer.Ordinal);
			public readonly System.Collections.Generic.List<System.IO.FileStream> Packages =
				new System.Collections.Generic.List<System.IO.FileStream>();
			public void Dispose()
			{
				foreach (var image in Images)
					image.Dispose();
				foreach (var symbol in Symbols.Values)
					symbol.Dispose();
				foreach (var package in Packages)
					package.Dispose();
			}
		}

		public bool Execute()
		{
			var leases = new Leases();
			bool registered = false;
			try
			{
				var engine = BuildEngine as Microsoft.Build.Framework.IBuildEngine4;
				if (engine == null)
					throw new System.InvalidOperationException("Build-lifetime input leases require IBuildEngine4.");
				if (VerifyOutputs)
				{
					var retained = engine.GetRegisteredTaskObject(LeaseKey,
						Microsoft.Build.Framework.RegisteredTaskObjectLifetime.Build) as Leases;
					if (retained == null || retained.PackageImages.Count == 0)
						throw new System.InvalidOperationException("Package has no retained validated input snapshots.");
					int normal = 0, symbols = 0;
					var seen = new System.Collections.Generic.HashSet<string>(System.StringComparer.Ordinal);
					foreach (var item in PackageFiles ?? new Microsoft.Build.Framework.ITaskItem[0])
					{
						var path = System.IO.Path.GetFullPath(item.ItemSpec);
						bool portable = path.EndsWith(".snupkg", System.StringComparison.OrdinalIgnoreCase);
						bool legacy = path.EndsWith(".symbols.nupkg", System.StringComparison.OrdinalIgnoreCase);
						if (!portable && !path.EndsWith(".nupkg", System.StringComparison.OrdinalIgnoreCase))
							continue; // The SDK also returns its intermediate nuspec.
						if (!seen.Add(path))
							throw new System.IO.InvalidDataException("Duplicate SDK package output: " + path);
						if (portable || legacy) symbols++; else normal++;
						VerifyPackage(retained, path, portable);
					}
					if (normal != 1 || symbols != (ExpectSymbols ? 1 : 0))
						throw new System.IO.InvalidDataException("SDK-resolved normal/symbol package output set is incomplete.");
					return true;
				}
				if (Images == null || Images.Length == 0)
					throw new System.InvalidOperationException("No managed runtime inputs were selected.");
				if (engine.GetRegisteredTaskObject(LeaseKey, Microsoft.Build.Framework.RegisteredTaskObjectLifetime.Build) != null)
					throw new System.InvalidOperationException("Managed inputs were already leased for this package.");
				foreach (var item in Images)
				{
					var image = ManagedImage.Open(item.ItemSpec, item.GetMetadata("AssemblyName"));
					leases.Images.Add(image);
					var packagePath = item.GetMetadata("PackagePath");
					if (!System.String.IsNullOrEmpty(packagePath))
						leases.PackageImages.Add(packagePath.Replace('\\', '/'), image);
					var producerPath = item.GetMetadata("ProducerReference");
					if (!System.String.IsNullOrEmpty(producerPath))
					{
						var producer = ManagedImage.Open(producerPath, item.GetMetadata("AssemblyName"));
						leases.Images.Add(producer);
						ManagedImage.AssertReferenceCopy(image, producer);
					}
					BuildEngine.LogMessageEvent(new Microsoft.Build.Framework.BuildMessageEventArgs(
						"Validated managed input " + image.Path + " SHA256=" + image.Sha256,
						null, nameof(ValidateManagedImages), Microsoft.Build.Framework.MessageImportance.Low));
				}
				foreach (var item in Symbols ?? new Microsoft.Build.Framework.ITaskItem[0])
				{
					var symbol = new Symbol
					{
						File = new System.IO.FileStream(item.ItemSpec, System.IO.FileMode.Open,
							System.IO.FileAccess.Read, System.IO.FileShare.Read)
					};
					try
					{
						using (var buffer = new System.IO.MemoryStream())
						{
							symbol.File.CopyTo(buffer);
							symbol.Bytes = buffer.ToArray();
						}
						var implementation = System.IO.Path.GetFullPath(item.GetMetadata("Implementation"));
						ManagedImage image = null;
						foreach (var candidate in leases.PackageImages.Values)
							if (candidate.Path == implementation) image = candidate;
						if (image == null) throw new System.IO.InvalidDataException("Symbol input has no validated implementation.");
						using (var input = image.OpenRead())
						using (var pdb = new System.IO.MemoryStream(symbol.Bytes, false))
							PortablePdb.AssertMatches(pdb, input);
						using (var hash = System.Security.Cryptography.SHA256.Create())
							symbol.Hash = System.BitConverter.ToString(hash.ComputeHash(symbol.Bytes)).Replace("-", "");
						leases.Symbols.Add(item.GetMetadata("PackagePath").Replace('\\', '/'), symbol);
					}
					catch { symbol.Dispose(); throw; }
				}
				// NuGet Pack reads these same paths later in this build. Do not release handles
				// on task return, or a writer could replace validated bytes before consumption.
				engine.RegisterTaskObject(LeaseKey, leases,
					Microsoft.Build.Framework.RegisteredTaskObjectLifetime.Build, false);
				registered = true;
				return true;
			}
			catch (System.Exception error)
			{
				// An integrity error is terminal, never a copy, rebuild, warning or retry.
				BuildEngine.LogErrorEvent(new Microsoft.Build.Framework.BuildErrorEventArgs(
					null, "UMRI001", null, 0, 0, 0, 0,
					"Managed artifact integrity validation failed: " + error.Message, null, nameof(ValidateManagedImages)));
				return false;
			}
			finally
			{
				if (!registered)
					leases.Dispose();
			}

			static void VerifyPackage(Leases leases, string packageFile, bool portable)
			{
				var stream = new System.IO.FileStream(packageFile, System.IO.FileMode.Open,
					System.IO.FileAccess.Read, System.IO.FileShare.Read);
				try
				{
					using (var archive = new System.IO.Compression.ZipArchive(stream,
						System.IO.Compression.ZipArchiveMode.Read, true))
					{
						var names = new System.Collections.Generic.HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
						foreach (var entry in archive.Entries)
						{
							var name = entry.FullName.Replace('\\', '/');
							if (name.StartsWith("/", System.StringComparison.Ordinal) || name.Contains(":") ||
								System.Array.IndexOf(name.Split('/'), "..") >= 0)
								throw new System.IO.InvalidDataException("Unsafe archive entry: " + name);
							if (!names.Add(name))
								throw new System.IO.InvalidDataException("Duplicate archive entry: " + entry.FullName);
							// Force decompression of all entries, not only the expected DLLs.
							using (var input = entry.Open()) input.CopyTo(System.IO.Stream.Null);
						}
						if (portable && leases.Symbols.Count == 0)
							throw new System.IO.InvalidDataException("Portable symbol package has no validated PDB inputs.");
						foreach (var pair in leases.PackageImages)
						{
							if (portable) continue;
							int count = 0;
							foreach (var entry in archive.Entries)
							{
								if (entry.FullName.Replace('\\', '/') != pair.Key)
									continue;
								count++;
								using (var input = entry.Open())
								using (var hash = System.Security.Cryptography.SHA256.Create())
								{
									if (entry.Length != pair.Value.Length ||
										System.BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "") != pair.Value.Sha256)
										throw new System.IO.InvalidDataException("Packaged bytes differ from validated input: " + pair.Key);
								}
							}
							if (count != 1)
								throw new System.IO.InvalidDataException("Missing or duplicated managed package entry: " + pair.Key);
						}
						foreach (var pair in leases.Symbols)
						{
							var entry = archive.GetEntry(pair.Key);
							if (entry == null)
								throw new System.IO.InvalidDataException("Missing validated PDB entry: " + pair.Key);
							using (var input = entry.Open())
							using (var hash = System.Security.Cryptography.SHA256.Create())
								if (entry.Length != pair.Value.Bytes.Length ||
									System.BitConverter.ToString(hash.ComputeHash(input)).Replace("-", "") != pair.Value.Hash)
									throw new System.IO.InvalidDataException("Packaged PDB differs from validated input: " + pair.Key);
						}
						foreach (var entry in archive.Entries)
						{
							if (entry.FullName.EndsWith(".pdb", System.StringComparison.OrdinalIgnoreCase) &&
								!leases.Symbols.ContainsKey(entry.FullName))
								throw new System.IO.InvalidDataException("Unvalidated PDB entry: " + entry.FullName);
							if ((entry.FullName.EndsWith(".dll", System.StringComparison.OrdinalIgnoreCase) ||
								entry.FullName.EndsWith(".exe", System.StringComparison.OrdinalIgnoreCase)) &&
								(portable || !leases.PackageImages.ContainsKey(entry.FullName)))
								throw new System.IO.InvalidDataException("Unvalidated implementation entry: " + entry.FullName);
						}
					}
					leases.Packages.Add(stream);
				}
				catch
				{
					stream.Dispose();
					throw;
				}
			}
		}
	}
}
