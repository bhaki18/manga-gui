//go:build windows

package main

import (
	"fmt"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"syscall"
	"unsafe"
)

var (
	user32          = syscall.NewLazyDLL("user32.dll")
	procMessageBoxW = user32.NewProc("MessageBoxW")
)

const (
	MB_OK              = 0x00000000
	MB_ICONINFORMATION = 0x00000040
	MB_ICONERROR       = 0x00000010
)

func messageBox(title, message string, flags uint32) {
	t, _ := syscall.UTF16PtrFromString(title)
	m, _ := syscall.UTF16PtrFromString(message)
	procMessageBoxW.Call(0, uintptr(unsafe.Pointer(m)), uintptr(unsafe.Pointer(t)), uintptr(flags))
}

func copyFile(src, dst string) error {
	in, err := os.Open(src)
	if err != nil {
		return err
	}
	defer in.Close()

	if err := os.MkdirAll(filepath.Dir(dst), 0755); err != nil {
		return err
	}

	out, err := os.Create(dst)
	if err != nil {
		return err
	}
	defer out.Close()

	_, err = io.Copy(out, in)
	return err
}

func copyDir(src, dst string) error {
	return filepath.Walk(src, func(path string, info os.FileInfo, err error) error {
		if err != nil {
			return err
		}
		rel, err := filepath.Rel(src, path)
		if err != nil {
			return err
		}
		if rel == "." {
			return nil
		}
		target := filepath.Join(dst, rel)
		if info.IsDir() {
			return os.MkdirAll(target, 0755)
		}
		return copyFile(path, target)
	})
}

func main() {
	silent := false
	for _, arg := range os.Args[1:] {
		low := strings.ToLower(arg)
		if low == "/s" || low == "/silent" || low == "--silent" || low == "-s" {
			silent = true
		}
	}

	exePath, err := os.Executable()
	if err != nil {
		if !silent {
			messageBox("Manga GUI Setup", "Impossibile rilevare il percorso dell'installer.", MB_ICONERROR)
		}
		return
	}
	sourceDir := filepath.Dir(exePath)

	localAppData := os.Getenv("LOCALAPPDATA")
	if localAppData == "" {
		userProfile := os.Getenv("USERPROFILE")
		if userProfile != "" {
			localAppData = filepath.Join(userProfile, "AppData", "Local")
		} else {
			localAppData = "C:\\ProgramData"
		}
	}
	installDir := filepath.Join(localAppData, "Programs", "MangaGui")

	cleanSrc, _ := filepath.Abs(sourceDir)
	cleanDst, _ := filepath.Abs(installDir)

	if cleanSrc != cleanDst {
		_ = os.MkdirAll(installDir, 0755)
		if err := copyDir(sourceDir, installDir); err != nil {
			if !silent {
				messageBox("Manga GUI Setup", fmt.Sprintf("Impossibile copiare i file dell'applicazione in %s:\n%v", installDir, err), MB_ICONERROR)
			}
			return
		}
	}

	targetExe := filepath.Join(installDir, "MangaGui.exe")
	iconPath := filepath.Join(installDir, "manga-gui.ico")
	if _, err := os.Stat(iconPath); err != nil {
		iconPath = targetExe
	}

	// Creazione collegamenti Desktop e Menu Start tramite PowerShell WScript.Shell
	psScript := fmt.Sprintf(`
$ws = New-Object -ComObject WScript.Shell
$desk = [Environment]::GetFolderPath('Desktop')
if ($desk) {
  $s1 = $ws.CreateShortcut("$desk\Manga GUI.lnk")
  $s1.TargetPath = '%s'
  $s1.WorkingDirectory = '%s'
  $s1.IconLocation = '%s,0'
  $s1.Description = 'Manga GUI - Downloader, Streamer e Reader'
  $s1.Save()
}
$start = [Environment]::GetFolderPath('Programs')
if ($start) {
  $s2 = $ws.CreateShortcut("$start\Manga GUI.lnk")
  $s2.TargetPath = '%s'
  $s2.WorkingDirectory = '%s'
  $s2.IconLocation = '%s,0'
  $s2.Description = 'Manga GUI - Downloader, Streamer e Reader'
  $s2.Save()
}
`, targetExe, installDir, iconPath, targetExe, installDir, iconPath)

	_ = exec.Command("powershell", "-NoProfile", "-ExecutionPolicy", "Bypass", "-WindowStyle", "Hidden", "-Command", psScript).Run()

	// Registrazione disinstallazione nel registro Windows
	regCmd := fmt.Sprintf(`reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MangaGui" /v "DisplayName" /t REG_SZ /d "Manga GUI" /f && reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MangaGui" /v "DisplayIcon" /t REG_SZ /d "%s" /f && reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MangaGui" /v "InstallLocation" /t REG_SZ /d "%s" /f && reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MangaGui" /v "Publisher" /t REG_SZ /d "Manga GUI" /f && reg add "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\MangaGui" /v "UninstallString" /t REG_SZ /d "cmd /c rmdir /s /q \"%s\"" /f`, iconPath, installDir, installDir)
	_ = exec.Command("cmd", "/c", regCmd).Run()

	if !silent {
		messageBox("Manga GUI Setup", "Installazione completata con successo!\n\nL'icona è stata creata sul Desktop e nel Menu Start.\nManga GUI verrà avviato ora.", MB_ICONINFORMATION)
	}

	cmd := exec.Command(targetExe)
	cmd.Dir = installDir
	_ = cmd.Start()
}

