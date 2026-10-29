use serde::Serialize;
use serde_json::{json, Value};
use std::io::{BufRead, BufReader, Write};
use std::process::{Command, Stdio};

#[cfg(target_os = "windows")]
use std::os::windows::process::CommandExt;

#[cfg(target_os = "windows")]
const CREATE_NO_WINDOW: u32 = 0x08000000;

fn hidden_command(program: &str) -> Command {
    let mut command = Command::new(program);
    #[cfg(target_os = "windows")]
    command.creation_flags(CREATE_NO_WINDOW);
    command
}

#[derive(Serialize)]
#[serde(rename_all = "camelCase")]
struct EnvironmentStatus {
    codex_installed: bool,
    codex_version: Option<String>,
    terminal_installed: bool,
    vscode_installed: bool,
}

fn command_available(program: &str) -> bool {
    hidden_command(program)
        .arg("--version")
        .stdout(Stdio::null())
        .stderr(Stdio::null())
        .status()
        .is_ok()
}

#[tauri::command]
fn environment_status() -> EnvironmentStatus {
    let codex_output = hidden_command("codex").arg("--version").output().ok();
    let codex_version = codex_output.as_ref().and_then(|output| {
        output.status.success().then(|| String::from_utf8_lossy(&output.stdout).trim().to_string())
    });
    EnvironmentStatus {
        codex_installed: codex_version.is_some(),
        codex_version,
        terminal_installed: command_available("wt.exe"),
        vscode_installed: command_available("code"),
    }
}

fn send_line(stdin: &mut impl Write, value: Value) -> Result<(), String> {
    serde_json::to_writer(&mut *stdin, &value).map_err(|error| error.to_string())?;
    stdin.write_all(b"\n").map_err(|error| error.to_string())?;
    stdin.flush().map_err(|error| error.to_string())
}

fn read_response(reader: &mut impl BufRead, expected_id: i64) -> Result<Value, String> {
    let mut line = String::new();
    loop {
        line.clear();
        if reader.read_line(&mut line).map_err(|error| error.to_string())? == 0 {
            return Err("Codex app-server wurde unerwartet beendet.".into());
        }
        let value: Value = match serde_json::from_str(line.trim()) {
            Ok(value) => value,
            Err(_) => continue,
        };
        if value.get("id").and_then(Value::as_i64) == Some(expected_id) {
            if let Some(error) = value.get("error") {
                return Err(error.to_string());
            }
            return value.get("result").cloned().ok_or_else(|| "Leere App-Server-Antwort.".into());
        }
    }
}

#[tauri::command]
fn list_threads(archived: bool) -> Result<Value, String> {
    let mut child = hidden_command("codex")
        .args(["app-server", "--listen", "stdio://"])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::null())
        .spawn()
        .map_err(|error| format!("Codex app-server konnte nicht gestartet werden: {error}"))?;

    let mut stdin = child.stdin.take().ok_or("Kein stdin vom Codex app-server.")?;
    let stdout = child.stdout.take().ok_or("Kein stdout vom Codex app-server.")?;
    let mut reader = BufReader::new(stdout);

    send_line(&mut stdin, json!({
        "method": "initialize",
        "id": 1,
        "params": { "clientInfo": { "name": "codex_cli_manager", "title": "Codex Session Manager", "version": env!("CARGO_PKG_VERSION") } }
    }))?;
    read_response(&mut reader, 1)?;
    send_line(&mut stdin, json!({ "method": "initialized", "params": {} }))?;
    send_line(&mut stdin, json!({
        "method": "thread/list",
        "id": 2,
        "params": { "limit": 500, "archived": archived, "sortKey": "recency_at", "sortDirection": "desc", "sourceKinds": [] }
    }))?;
    let result = read_response(&mut reader, 2);
    let _ = child.kill();
    result
}

#[tauri::command]
fn resume_session(thread_id: String, cwd: String) -> Result<(), String> {
    if cwd.is_empty() {
        return Err("Die Session enthält kein Arbeitsverzeichnis.".into());
    }
    hidden_command("wt.exe")
        .args(["-d", &cwd, "codex", "resume", &thread_id])
        .spawn()
        .map(|_| ())
        .map_err(|error| format!("Windows Terminal konnte nicht geöffnet werden: {error}"))
}

#[tauri::command]
fn open_project(cwd: String) -> Result<(), String> {
    if cwd.is_empty() {
        return Err("Die Session enthält kein Arbeitsverzeichnis.".into());
    }
    hidden_command("code")
        .arg(cwd)
        .spawn()
        .map(|_| ())
        .map_err(|error| format!("VS Code konnte nicht geöffnet werden: {error}"))
}

#[tauri::command]
fn archive_session(thread_id: String) -> Result<(), String> {
    let output = hidden_command("codex")
        .args(["archive", &thread_id])
        .output()
        .map_err(|error| format!("Codex konnte nicht gestartet werden: {error}"))?;
    if output.status.success() {
        Ok(())
    } else {
        Err(String::from_utf8_lossy(&output.stderr).trim().to_string())
    }
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![
            environment_status,
            list_threads,
            resume_session,
            open_project,
            archive_session
        ])
        .run(tauri::generate_context!())
        .expect("error while running Codex Session Manager");
}
