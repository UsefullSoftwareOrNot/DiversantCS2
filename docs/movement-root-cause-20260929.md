# Movement at zero health: build 14186

## Evidence and limits

The movement capture shows input commands advancing while coordinates remain fixed. The health experiment recorded HP=10000 once, then HP=0 at the next sample 65 ms later. This is a sampling interval, not the duration of the override. No movement input was held in the HP=10000 sample.

Static inspection confirms matching zero-health movement suppression in the installed client and server libraries. Forward/left/up command values feed working-data offsets 0x2c/0x30/0x34; the zero-health branch clears these fields. The server independently reads its own CBaseEntity.m_iHealth at 0x2d0. The client reads C_BaseEntity.m_iHealth at 0x34c.

The server branch also suppresses movement for flag 0x20. It exempts an observer-service mode equal to 4. These statements describe code in the installed DLL, not a runtime trace from an official server.

The previous apparent schema mismatch is resolved: server health is accessed by a separate getter, not by an inline comparison. The getter uses the schema offset 0x2d0.

Changing only client HP does not alter the health consulted by the server. The installed server code supports a server-side explanation for rejected movement at zero server health. Official-server HP, execution of that branch in the reported match, and the writer that resets client HP are not directly observed. Network replication versus prediction rollback cannot be distinguished by the existing samples. Shooting/damage acceptance was reported by the user; it is not evidence that movement is accepted.

No executable patches or new health writes were performed during this investigation.

## client.dll

SHA256: `5440adef04e91c282e8d9815f51f8255ad95740920c9e8660fa1ddbb3c07a1f5`

```text

00a7503a  movss xmm0, dword ptr [rsi + 0x1a0]

00a75042  call 0xa78250

00a75047  movss dword ptr [rbp + 0x2c], xmm0

00a7504c  movss dword ptr [rbp + 0x20], xmm0

00a75051  mov eax, dword ptr [rsi + 0x58]

00a75054  test rax, 0x600

00a7505a  je 0xa75073

00a7505c  movss xmm0, dword ptr [rsi + 0x1a4]

00a75064  call 0xa78250

00a75069  movss dword ptr [rbp + 0x30], xmm0

00a7506e  movss dword ptr [rbp + 0x24], xmm0

00a75073  movss xmm0, dword ptr [rsi + 0x1a8]

00a7507b  call 0xa78250

00a75080  movss dword ptr [rbp + 0x34], xmm0

00a75085  movss dword ptr [rbp + 0x28], xmm0

```

```text

008c527f  mov rax, qword ptr [r14]

008c5282  xor r15d, r15d

008c5285  test byte ptr [rax + 0x3f4], 0x20

008c528c  jne 0x8c52a7

008c528e  test rax, rax

008c5291  jne 0x8c529b

008c5293  mov rcx, rdi

008c5296  call 0xa7d3c0

008c529b  mov rax, qword ptr [r14]

008c529e  cmp dword ptr [rax + 0x34c], r15d

008c52a5  jg 0x8c52c5

008c52a7  test rbx, rbx

008c52aa  je 0x8c52bd

008c52ac  mov rax, qword ptr [rbx]

008c52af  mov rcx, rbx

008c52b2  call qword ptr [rax + 0xf0]

008c52b8  cmp eax, 4

008c52bb  je 0x8c52c5

008c52bd  mov qword ptr [rsi + 0x2c], r15

008c52c1  mov dword ptr [rsi + 0x34], r15d

008c52c5  cmp qword ptr [r14], r15

008c52c8  jne 0x8c52d2

008c52ca  mov rcx, rdi

008c52cd  call 0xa7d3c0

008c52d2  mov rcx, qword ptr [rdi + 0x38]

008c52d6  cmp dword ptr [rcx + 0x34c], r15d

008c52dd  jle 0x8c53bf

```

## server.dll

SHA256: `f95fe0dcd7b526137a8b305dd76a72f624e0508ad42bb5949d05c77a37e1bd70`

```text

00cf2100  mov eax, dword ptr [rcx + 0x2d0]

00cf2106  ret

```

```text

00ab86c2  mov rax, qword ptr [r14]

00ab86c5  xor r15d, r15d

00ab86c8  test byte ptr [rax + 0x388], 0x20

00ab86cf  jne 0xab86ea

00ab86d1  test rax, rax

00ab86d4  jne 0xab86de

00ab86d6  mov rcx, rdi

00ab86d9  call 0xc80730

00ab86de  mov rcx, qword ptr [r14]

00ab86e1  call 0xcf2100

00ab86e6  test eax, eax

00ab86e8  jg 0xab8708

00ab86ea  test rbx, rbx

00ab86ed  je 0xab8700

00ab86ef  mov rax, qword ptr [rbx]

00ab86f2  mov rcx, rbx

00ab86f5  call qword ptr [rax + 0xb8]

00ab86fb  cmp eax, 4

00ab86fe  je 0xab8708

00ab8700  mov qword ptr [rsi + 0x2c], r15

00ab8704  mov dword ptr [rsi + 0x34], r15d

00ab8708  cmp qword ptr [r14], r15

00ab870b  jne 0xab8715

00ab870d  mov rcx, rdi

00ab8710  call 0xc80730

00ab8715  mov rcx, qword ptr [rdi + 0x38]

00ab8719  call 0xcf2100

00ab871e  test eax, eax

00ab8720  jle 0xab8805

```
