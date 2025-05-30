#include <windows.h>
#include <stdio.h>
#include <errno.h>

#define EXPORT __declspec(dllexport)

struct sockaddr;

EXPORT __declspec(naked) int l_mkdir(const char* pathname, unsigned int mode) {
    __asm__ (
            "push ebx\n\t"
            "mov eax, 0x27\n\t"
            "mov ebx, [esp + 4 + 4]\n\t"
            "mov ecx, [esp + 4 + 8]\n\t"
            "int 0x80\n\t"
            "pop ebx\n\t"
            "ret"
            );
}

EXPORT __declspec(naked) int l_rmdir(const char* pathname) {
    __asm__ (
            "push ebx\n\t"
            "mov eax, 0x28\n\t"
            "mov ebx, [esp + 4 + 4]\n\t"
            "int 0x80\n\t"
            "pop ebx\n\t"
            "ret"
            );
}

EXPORT __declspec(naked) unsigned int l_getpid() {
    __asm__ (
            "mov eax, 0x14\n\t"
            "int 0x80\n\t"
            "ret"
            );
}

EXPORT __declspec(naked) int l_close(int fd) {
    __asm__ (
            "push ebx\n\t"
            "mov eax, 0x06\n\t"
            "mov ebx, [esp + 4 + 4]\n\t"
            "int 0x80\n\t"
            "pop ebx\n\t"
            "ret"
            );
}

EXPORT __declspec(naked) int l_socketcall(int call, void* args) {
    __asm__ (
            "push ebx\n\t"
            "mov eax, 0x66\n\t"
            "mov ebx, [esp + 4 + 4]\n\t"
            "mov ecx, [esp + 4 + 8]\n\t"
            "int 0x80\n\t"
            "pop ebx\n\t"
            "ret"
            );
}

EXPORT __declspec(naked) int l_fcntl(unsigned int fd, unsigned int cmd, unsigned long arg) {
    __asm__ (
            "push ebx\n\t"
            "mov eax, 0x37\n\t"
            "mov ebx, [esp + 4 + 4]\n\t"
            "mov ecx, [esp + 4 + 8]\n\t"
            "mov edx, [esp + 4 + 12]\n\t"
            "int 0x80\n\t"
            "pop ebx\n\t"
            "ret"
            );
}

EXPORT __declspec(naked) int l_open(const char* filename, int flags, int mode) {
    __asm__ (
            "push ebx\n\t"
            "mov eax, 0x05\n\t"
            "mov ebx, [esp + 4 + 4]\n\t"
            "mov ecx, [esp + 4 + 8]\n\t"
            "mov edx, [esp + 4 + 12]\n\t"
            "int 0x80\n\t"
            "pop ebx\n\t"
            "ret"
            );
}

EXPORT __declspec(naked) int l_write(unsigned int fd, const char* buf, unsigned int count) {
    __asm__ (
            "push ebx\n\t"
            "mov eax, 0x04\n\t"
            "mov ebx, [esp + 4 + 4]\n\t"
            "mov ecx, [esp + 4 + 8]\n\t"
            "mov edx, [esp + 4 + 12]\n\t"
            "int 0x80\n\t"
            "pop ebx\n\t"
            "ret"
            );
}

EXPORT __declspec(naked) int l_read(unsigned int fd, char* buf, unsigned int count) {
    __asm__ (
            "push ebx\n\t"
            "mov eax, 0x03\n\t"
            "mov ebx, [esp + 4 + 4]\n\t"
            "mov ecx, [esp + 4 + 8]\n\t"
            "mov edx, [esp + 4 + 12]\n\t"
            "int 0x80\n\t"
            "pop ebx\n\t"
            "ret"
            );
}

EXPORT int l_socket(int domain, int type, int protocol) {
    void* args[3];
    args[0] = (void*)(int*)domain;
    args[1] = (void*)(int*)type;
    args[2] = (void*)(int*)protocol;
    return l_socketcall(1, args);
}

EXPORT int l_connect(int sockfd, const struct sockaddr *addr, unsigned int addrlen) {
    void* args[3];
    args[0] = (void*)(int*)sockfd;
    args[1] = (void*)addr;
    args[2] = (void*)(int*)addrlen;
    int result = l_socketcall(3, args);

    if (result < 0) {
        return -errno;
    }

    return result;
}

EXPORT int l_write_errno(unsigned int fd, const char* buf, unsigned int count) {
    int result = l_write(fd, buf, count);

    if (result < 0) {
        return -errno;
    }

    return result;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID lpReserved) {
    switch (ul_reason_for_call) {
    case DLL_PROCESS_ATTACH:
    case DLL_THREAD_ATTACH:
    case DLL_THREAD_DETACH:
    case DLL_PROCESS_DETACH:
        break;
    }
    return TRUE;
}
