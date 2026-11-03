#!/usr/bin/env bash
set -euo pipefail

usage() {
    echo "Usage: bash scripts/windows-test-vm.sh [path/to/Windows11.iso]"
    echo "The ISO is required when creating the VM; omit it for later starts."
}

if [[ ${1:-} == "-h" || ${1:-} == "--help" ]]; then
    usage
    exit 0
fi
if (( $# > 1 )); then
    usage >&2
    exit 2
fi

iso=${1:-}
if [[ -n $iso && ! -r $iso ]]; then
    echo "Windows installation ISO is not readable: $iso" >&2
    exit 1
fi
for command in qemu-system-x86_64 qemu-img swtpm flock; do
    if ! command -v "$command" >/dev/null 2>&1; then
        echo "Missing required command: $command" >&2
        exit 1
    fi
done
if [[ ! -r /dev/kvm || ! -w /dev/kvm ]]; then
    echo "The current user needs read/write access to /dev/kvm." >&2
    exit 1
fi

code=/usr/share/OVMF/OVMF_CODE_4M.ms.fd
vars_template=/usr/share/OVMF/OVMF_VARS_4M.ms.fd
if [[ ! -r $code || ! -r $vars_template ]]; then
    echo "OVMF secure-boot firmware is missing; install the ovmf package." >&2
    exit 1
fi

vm_dir=${MMAWAY_VM_DIR:-"$HOME/.local/share/mmaway-test-vm"}
disk=$vm_dir/windows.qcow2
vars=$vm_dir/OVMF_VARS.fd
if [[ $vm_dir == *,* || $iso == *,* ]]; then
    echo "VM and ISO paths cannot contain commas in QEMU drive options." >&2
    exit 1
fi
if [[ ! -e $disk && -z $iso ]]; then
    echo "A Windows 11 installation ISO is required for the first start." >&2
    exit 1
fi
mkdir -p "$vm_dir/tpm"
exec 9>"$vm_dir/lock"
if ! flock -n 9; then
    echo "The VM is already running or being started: $vm_dir" >&2
    exit 1
fi
if [[ ! -e $disk ]]; then
    qemu-img create -f qcow2 "$disk" 80G
fi
if [[ ! -e $vars ]]; then
    cp -- "$vars_template" "$vars"
fi

socket_dir=$(mktemp -d "${TMPDIR:-/tmp}/mmaway-tpm.XXXXXXXX")
tpm_pid=
cleanup() {
    if [[ -n $tpm_pid ]]; then
        kill "$tpm_pid" 2>/dev/null || true
        wait "$tpm_pid" 2>/dev/null || true
    fi
    rm -f -- "$socket_dir/swtpm.sock"
    rmdir -- "$socket_dir"
}
trap cleanup EXIT

socket=$socket_dir/swtpm.sock
swtpm socket --tpm2 --tpmstate "dir=$vm_dir/tpm" \
    --ctrl "type=unixio,path=$socket" &
tpm_pid=$!
for (( attempt=0; attempt<50; attempt++ )); do
    if [[ -S $socket ]]; then
        break
    fi
    if ! kill -0 "$tpm_pid" 2>/dev/null; then
        echo "The virtual TPM failed to start." >&2
        exit 1
    fi
    sleep 0.1
done
if [[ ! -S $socket ]]; then
    echo "Timed out waiting for the virtual TPM." >&2
    exit 1
fi

args=(
    -enable-kvm -machine q35,smm=on -cpu host -smp 4 -m 8192
    -drive "if=pflash,format=raw,unit=0,readonly=on,file=$code"
    -drive "if=pflash,format=raw,unit=1,file=$vars"
    -drive "if=ide,format=qcow2,file=$disk"
    -chardev "socket,id=chrtpm,path=$socket"
    -tpmdev emulator,id=tpm0,chardev=chrtpm
    -device tpm-tis,tpmdev=tpm0
    -device qemu-xhci -device usb-tablet
    -netdev user,id=net0,hostfwd=tcp:127.0.0.1:33989-:3389
    -device e1000e,netdev=net0
    -display "${MMAWAY_VM_DISPLAY:-gtk}"
)
if [[ -n $iso ]]; then
    args+=(-drive "if=ide,media=cdrom,readonly=on,file=$iso" -boot order=d)
fi
if [[ -f $vm_dir/mmaway-share.iso ]]; then
    args+=(-drive "if=ide,media=cdrom,readonly=on,file=$vm_dir/mmaway-share.iso")
fi

echo "Starting VM from $vm_dir. The optional RDP port is available only on 127.0.0.1:33989."
qemu-system-x86_64 "${args[@]}"
