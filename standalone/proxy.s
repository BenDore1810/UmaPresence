.intel_syntax noprefix
.text
.macro proxy name, target
.globl \name
.def \name; .scl 2; .type 32; .endef
\name:
    sub rsp, 200
    mov [rsp+32], rcx
    mov [rsp+40], rdx
    mov [rsp+48], r8
    mov [rsp+56], r9
    movups [rsp+64], xmm0
    movups [rsp+80], xmm1
    movups [rsp+96], xmm2
    movups [rsp+112], xmm3
    call runtime_bootstrap
    mov rcx, [rsp+32]
    mov rdx, [rsp+40]
    mov r8, [rsp+48]
    mov r9, [rsp+56]
    movups xmm0, [rsp+64]
    movups xmm1, [rsp+80]
    movups xmm2, [rsp+96]
    movups xmm3, [rsp+112]
    add rsp, 200
    mov rax, qword ptr [rip+\target]
    test rax, rax
    jz 1f
    jmp rax
1:  xor eax, eax
    ret
.endm
proxy UnityMain, original_unitymain
proxy UnityMain2, original_unitymain2
.section .drectve,"yn"
.ascii " /EXPORT:UnityMain /EXPORT:UnityMain2"
