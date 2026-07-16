# Vcrypt 🔒

Vcrypt is a premium, open-source Windows desktop application that turns any standard USB Pendrive into a highly secure, encrypted vault. It works by physically restructuring the USB drive into a "Public" partition and a hidden "SecureVault" partition, mapping the secure side directly to a seamless `Z:` Network Drive using on-the-fly AES-256 encryption.

## 🚀 Features
- **Glassmorphic UI:** A beautiful, dark-mode WPF interface with sleek animations and responsive progress indicators.
- **Hardware-Level Concealment:** Modifies the USB Master Boot Record (MBR) to completely hide the secure partition from the OS and file explorers.
- **On-the-Fly AES-256 Encryption:** Files are not just hidden; dropping them into the `Z:` drive instantly encrypts them block-by-block in real-time.
- **Zero UAC Spam:** Engineered with a unified administrator manifest to handle complex disk operations silently without bothering the user.
- **True Portability:** Vcrypt copies its own standalone `.exe` to the public side of your USB drive, allowing you to plug it into any Windows PC and unlock your vault instantly without installing dependencies.

---

## 🛠️ Technologies Used

- **C# .NET 8.0 WPF:** The core framework powering the desktop application.
- **CommunityToolkit.Mvvm:** Used for clean, modern Model-View-ViewModel architecture.
- **MaterialDesignThemes:** Provided the premium UI components, typography, and styling.
- **NWebDav.Server:** A lightweight WebDAV server library used to create the virtual network drive.
- **PowerShell Interop:** Leveraged `System.Diagnostics.Process` to inject and execute raw hardware manipulation scripts (`DiskPart`, `Get-Partition`, `mountvol`).
- **WMI (Windows Management Instrumentation):** Used `ManagementEventWatcher` to listen for hardware `__InstanceCreationEvent`s, allowing real-time detection of USB drive insertions.
- **System.Security.Cryptography (AES):** Used `CryptoStream` for seamless, on-the-fly encryption and decryption of files streaming through the WebDAV server.

---

## 🧗‍♂️ Struggles Faced & How We Rectified Them

Building a low-level hardware manipulation tool with a seamless, user-friendly UI came with massive technical hurdles. Here are the biggest struggles we faced and how we solved them in great detail:

### 1. The Seamless Drive Mapping Struggle
**The Problem:** We wanted the user to interact with their encrypted files natively in Windows Explorer (like a real hard drive). However, Windows does not easily let you mount a virtual file system to a top-level drive letter without complex kernel-mode drivers (like Dokany/FUSE) or legacy, unprofessional `subst` commands. 
**The Solution:** We implemented a local WebDAV server directly inside the app. When the user unlocks the vault, Vcrypt starts an HTTP server on `localhost`. However, Windows blocks local Basic Auth by default. To fix this, we wrote a dynamic registry patcher (`WebDavRegistryPatcher.cs`) to allow `BasicAuthLevel = 2`, and then used the `net use Z: http://localhost:11111/vault` command to mount our custom AES-streaming `VaultStore` as a native Network Drive.

### 2. The "Access is Denied" PowerShell Bleed
**The Problem:** During the format process, we used `mountvol /N` to stop Windows from eagerly assigning drive letters to incomplete partitions. Suddenly, the app started failing with a bizarre `"Signature file missing"` error, and looking for paths like `Access is denied.\nF:\.Vcrypt`.
**The Solution:** Because of inherited privilege drops, `powershell.exe` was bleeding `Access is denied` warnings directly into the standard output stream, completely corrupting the C# string variables that were trying to parse the drive letter! We rectified this by hardening the C# parsing logic to strictly split the output array and extract only the final valid character, piping noisy PowerShell commands to `Out-Null`, and embedding a root `<requestedExecutionLevel level="requireAdministrator" />` manifest so the entire process tree securely inherits UAC elevation.

### 3. File Explorer Caching vs. Partition Hiding
**The Problem:** When converting the second partition to a hidden type (`Set-Partition -MbrType 23`), Windows File Explorer and the Plug-and-Play (PnP) manager would cache the drive's state. If we immediately checked for our `.Vcrypt` signature file using `File.Exists()`, Windows would confidently lie and say the file didn't exist because the PnP manager hadn't fully mounted the filesystem yet.
**The Solution:** We integrated Windows Service manipulation directly into our PowerShell formatting routines. By forcefully executing `Stop-Service -Name ShellHWDetection` before disk operations, and `Start-Service` in a `finally` block, we forced Windows to completely flush its hardware cache and properly remount the volumes. We also shifted drive letter detection away from unreliable volume labels directly to explicit `Get-Partition -DiskNumber -PartitionNumber 1` queries.

### 4. Progress Bar UI Thread Blocking
**The Problem:** Because disk formatting and WebDAV mounting are heavy, synchronous I/O operations, they were completely freezing the WPF UI thread, causing the progress bars to stutter and the "glassmorphic" animations to lag.
**The Solution:** We heavily refactored the infrastructure to use strict `async/await` patterns with `IProgress<T>`. We offloaded all PowerShell process executions to `Task.Run` and used the MVVM Toolkit's `Dispatcher.Invoke` to push progress percentage updates back to the UI thread, ensuring the application remained buttery smooth even while wiping a 64GB flash drive.

---

## 💻 Developer Guide: How to Build & Run

### Requirements
- **OS:** Windows 10 or Windows 11 (Requires Windows-specific APIs).
- **SDK:** [.NET 8.0 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)
- **IDE:** Visual Studio 2022 (recommended) or VS Code.

### Build Instructions
1. **Clone the Repository:**
   ```bash
   git clone https://github.com/vijaymachkuri/Vcrypt.git
   cd Vcrypt
   ```

2. **Restore Dependencies & Build (Debug):**
   ```bash
   dotnet build
   ```

3. **Run the Application locally:**
   *Note: You must run your terminal or IDE as Administrator to test hardware formatting!*
   ```bash
   cd Vcrypt.UI
   dotnet run
   ```

4. **Publish the Standalone Executable (Release):**
   To generate the single portable `Vcrypt.exe` file:
   ```bash
   cd Vcrypt.UI
   dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o "C:\Path\To\Your\Output\Folder"
   ```
