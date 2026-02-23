# Phase 3 Work Orders: Interface & Integrity

## 🔴 Agent 1: Lane 1 - Core Integrity (EVM Stability)

**Role:** Senior Logic Engineer
**Objective:** Harden the EVM against crashes and standardize error reporting.
**Governance Override:**

1. **Strict 50-line limit.**
2. **No Exceptions:** Flow control must not use `try/catch` for logic. Use Result patterns.

**Tasks:**

1. **Refactor Execution:** Change opcode return types to `ExecutionResult` (Gas, Logs, Error).
2. **Safe Math Wrappers:** detailed check of `ADD/MUL/SUB` for overflows (EVM wrapping behavior vs .NET exceptions).
3. **Error Hierarchy:** Implement `EvmError` type (StackUnderflow, BadJumpDest, OutOfGas).
4. **Verification:** Add 5 "Negative Tests" that deliberately fail execution and verify the `ExecutionResult` contains the correct error code without crashing the process.

---

## 🔵 Agent 2: Phase 2 Completion & RPC Hardening

**Role:** Middleware Architect
**Objective:** Finish Global State/Mempool (Phase 2 completion) AND add RPC Error Handling.
**Governance Override:**

1. **Strict 50-line limit.**
2. **Middleware Pattern:** Use ASP.NET Middleware for global error catching.

**Tasks:**

1. **Finish Phase 2:** Complete `GlobalState` & `TxMempool` as per previous order (if not fully done).
2. **Global Exception Handler:** Implement `JsonRpcExceptionFilter` to catch unhandled errors and return `{"error": {"code": -32603}}` JSON.
3. **Structured Logger:** Implement an `ObservableLogger` that raises C# events on new log lines (this is crucial for the GUI to see logs).
4. **Deliverable:** A `TxMempool` that orders transactions, and an RPC server that returns polite JSON errors instead of 500 HTML pages.

---

## 🟣 Agent 3: Lane 3 - The Frontend (WPF GUI)

**Role:** Senior Windows UI Engineer
**Objective:** Create the `Scrutor.UI` visual control plane.
**Governance Override:**

1. **MVVM Pattern:** Strict separation of View and ViewModel.
2. **Modern UI:** Use `WPF UI` or standard styling to look generic but clean (Dark Mode).

**Tasks:**

1. **New Project:** Initialize `Scrutor.UI` (WPF Application .NET 8).
2. **Node Hosting:** Implement `NodeHostService` in the UI project that wraps `Scrutor.CLI` startup logic but running as a background task.
3. **Dashboard View:** Create `MainWindow.xaml` with:
    * **Start/Stop Toggle:** Controls the background node.
    * **Config Form:** Inputs for Port, Network ID, Mining Mode.
    * **Log Stream:** A text box bound to Agent 2's `ObservableLogger`.
4. **Deliverable:** A working Windows app that can start the node and display "Node Started" in its internal log window.
