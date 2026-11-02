//go:build !windows

package main

import "fmt"

func main() {
	fmt.Println("This installer is intended to run on Windows only.")
}
