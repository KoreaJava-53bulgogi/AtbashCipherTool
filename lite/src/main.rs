// Windows 에서 실행할 때 검은 콘솔 창이 뜨지 않게 (디버그 빌드는 오류를 보려고 콘솔을 남김)
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

slint::include_modules!();

use std::cell::RefCell;
use std::path::PathBuf;
use std::rc::Rc;

use atbash_lite::core::engine::{self, Mode, Parameter};
use atbash_lite::core::{from_units, to_units};
use atbash_lite::i18n::{Language, Texts};
use atbash_lite::settings::{self, Settings};
use atbash_lite::update;
use slint::{ComponentHandle, ModelRc, SharedString, VecModel};

const VERSION: &str = env!("CARGO_PKG_VERSION");

thread_local! {
    /// 다른 스레드(업데이트 확인)의 결과를 화면 스레드에서 처리할 때 상태를 찾는 곳
    static STATE: RefCell<Option<Rc<RefCell<State>>>> = const { RefCell::new(None) };
}

struct State {
    settings: Settings,
    path: PathBuf,
    texts: Texts,
}

impl State {
    fn save(&self) {
        // 저장에 실패해도 앱은 계속 씀 (다음에 기본값으로 시작할 뿐)
        let _ = self.settings.save(&self.path);
    }

    fn language(&self) -> Language {
        Language::parse(&self.settings.language)
    }
}

fn model(items: impl IntoIterator<Item = String>) -> ModelRc<SharedString> {
    ModelRc::new(VecModel::from(items.into_iter().map(SharedString::from).collect::<Vec<_>>()))
}

fn current_mode(ui: &AppWindow) -> Mode {
    Mode::from_index(ui.get_mode_index()).unwrap_or(Mode::Atbash)
}

/// 화면의 글자를 지금 언어로
fn apply_language(ui: &AppWindow, state: &State) {
    let t = &state.texts;
    let menu = |key: &str| t.get(key).trim_end_matches("...").trim_end_matches('…').to_string();
    ui.set_labels(Labels {
        title: format!("{} Lite v{VERSION}", t.get("WINDOW_TITLE")).into(),
        input: t.get("INPUT_LABEL").into(),
        output: t.get("OUTPUT_LABEL").into(),
        mode: t.get("CIPHER_LABEL").into(),
        parameter: "".into(),
        transform: t.get("TRANSFORM_BUTTON").into(),
        copy: t.get("COPY_BUTTON").into(),
        clear: t.get("CLEAR_BUTTON").into(),
        load: menu("LOAD_MENU").into(),
        save: menu("SAVE_MENU").into(),
        dark: t.get("DARK_MODE_MENU").into(),
        update: t.get("CHECK_UPDATE_MENU").into(),
    });
    let mode_index = ui.get_mode_index();
    ui.set_mode_names(model(Mode::ALL.iter().map(|m| t.get(m.name_key()))));
    ui.set_mode_index(mode_index);
    ui.set_language_names(model(Language::ALL.iter().map(|l| l.native_name().to_string())));
    ui.set_language_index(Language::ALL.iter().position(|&l| l == state.language()).unwrap_or(0) as i32);
    ui.set_feedback("".into());
    update_parameter(ui, state);
}

/// 변환 방식에 맞게 이동 값/지름/키워드/비밀번호 칸을 보여줌
fn update_parameter(ui: &AppWindow, state: &State) {
    let mode = current_mode(ui);
    let (kind, label) = match mode.parameter() {
        Parameter::None => (0, ""),
        Parameter::Shift => (1, "PARAM_SHIFT_KEY"),
        Parameter::Diameter => (1, "PARAM_DIAMETER"),
        Parameter::Keyword => (2, "PARAM_KEYWORD"),
        Parameter::Password => (3, "PARAM_PASSWORD"),
    };
    let mut labels = ui.get_labels();
    labels.parameter = if label.is_empty() { "".into() } else { state.texts.get(label).into() };
    ui.set_labels(labels);
    ui.set_parameter_kind(kind);
    let s = &state.settings;
    match mode {
        Mode::ScytaleEncrypt | Mode::ScytaleDecrypt => {
            ui.set_number_minimum(1);
            ui.set_number_value(s.scytale_diameter);
        }
        Mode::HangulEncrypt | Mode::HangulDecrypt => {
            ui.set_number_minimum(-1000);
            ui.set_number_value(s.hangul_shift);
        }
        _ => {
            ui.set_number_minimum(-1000);
            ui.set_number_value(s.caesar_shift);
        }
    }
}

fn show_feedback(ui: &AppWindow, text: String) {
    ui.set_feedback(text.into());
    let weak = ui.as_weak();
    slint::Timer::single_shot(std::time::Duration::from_secs(2), move || {
        if let Some(ui) = weak.upgrade() {
            ui.set_feedback("".into());
        }
    });
}

fn error_box(title: &str, message: String) {
    rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Error)
        .set_title(title)
        .set_description(message)
        .show();
}

fn info_box(title: &str, message: String) {
    rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Info)
        .set_title(title)
        .set_description(message)
        .show();
}

/// 업데이트 확인: 인터넷에서 읽는 동안 화면이 멈추지 않게 따로 실행. `manual` 이면 결과를 항상 알려줌
fn start_update_check(ui: &AppWindow, manual: bool) {
    let weak = ui.as_weak();
    std::thread::spawn(move || {
        let result = update::fetch_manifest().and_then(|json| update::select(&json));
        let _ = weak.upgrade_in_event_loop(move |_ui| {
            if let Some(state) = STATE.with(|s| s.borrow().clone()) {
                handle_update(&state, result, manual);
            }
        });
    });
}

fn handle_update(state: &Rc<RefCell<State>>, result: Result<Option<update::Release>, String>, manual: bool) {
    // 창을 띄우는 동안 상태를 붙잡고 있지 않도록 필요한 글자를 먼저 꺼내 둠
    let (title, skipped, t) = {
        let mut st = state.borrow_mut();
        if result.is_ok() {
            st.settings.last_update_check = update::now_ms();
            st.save();
        }
        let get = |k: &str| st.texts.get(k);
        let t = [
            get("CHECK_UPDATE_MENU"),
            get("UPDATE_CHECK_FAILED"),
            get("UPDATE_LATEST_MESSAGE"),
            get("UPDATE_AVAILABLE_MESSAGE"),
            get("UPDATE_DOWNLOAD"),
            get("UPDATE_LATER"),
            get("UPDATE_SKIP"),
        ];
        (st.texts.get("UPDATE_AVAILABLE_TITLE"), st.settings.skipped_version.clone(), t)
    };
    let [check_title, failed, latest, available, download, later, skip] = t;

    let release = match result {
        Err(_) => {
            if manual {
                info_box(&check_title, failed);
            }
            return;
        }
        Ok(r) => r,
    };
    let Some(release) = release.filter(|r| update::compare_versions(&r.version, VERSION).is_gt()) else {
        if manual {
            info_box(&check_title, latest.replace("{0}", VERSION));
        }
        return;
    };
    if !manual && skipped.as_deref() == Some(release.version.as_str()) {
        return; // 자동 확인에서는 "건너뛰기" 한 버전을 다시 알리지 않음
    }

    let mut message = available.replace("{0}", &release.version).replace("{1}", VERSION);
    if !release.notes.trim().is_empty() {
        message = format!("{message}\n\n{}", release.notes.trim());
    }
    let picked = rfd::MessageDialog::new()
        .set_level(rfd::MessageLevel::Info)
        .set_title(&title)
        .set_description(message)
        .set_buttons(rfd::MessageButtons::YesNoCancelCustom(download.clone(), later, skip.clone()))
        .show();
    match picked {
        rfd::MessageDialogResult::Custom(choice) if choice == download => update::open_in_browser(&release.url),
        rfd::MessageDialogResult::Custom(choice) if choice == skip => {
            let mut st = state.borrow_mut();
            st.settings.skipped_version = Some(release.version);
            st.save();
        }
        _ => {}
    }
}

fn main() -> Result<(), slint::PlatformError> {
    let path = settings::default_path();
    let loaded = Settings::load(&path);
    let texts = Texts::load(Language::parse(&loaded.language));
    let state = Rc::new(RefCell::new(State { settings: loaded, path, texts }));
    STATE.with(|s| *s.borrow_mut() = Some(state.clone()));

    let ui = AppWindow::new()?;
    {
        let st = state.borrow();
        ui.set_mode_index(Mode::from_index(st.settings.cipher_mode).map_or(0, |m| m as i32));
        ui.set_dark(st.settings.dark);
        apply_language(&ui, &st);
    }

    // [변환]: AES 는 키를 만드는 데 시간이 걸리므로 화면이 멈추지 않게 따로 실행
    ui.on_transform({
        let weak = ui.as_weak();
        let state = state.clone();
        move || {
            let ui = weak.unwrap();
            let mode = current_mode(&ui);
            let input = to_units(&ui.get_input_text());
            let key = if ui.get_parameter_kind() == 1 { ui.get_number_value() } else { 0 };
            let keyword_text = if mode.is_aes() { ui.get_password() } else { ui.get_keyword() };
            let keyword = to_units(&keyword_text);
            let st = state.borrow();
            let messages: Vec<(engine::TransformError, String)> = [
                engine::TransformError::DecodeFailed,
                engine::TransformError::AesNoPassword,
                engine::TransformError::AesDecryptFailed,
            ]
            .into_iter()
            .map(|e| (e, st.texts.get(e.message_key())))
            .collect();
            let describe = move |result: Result<Vec<u16>, engine::TransformError>| match result {
                Ok(units) => from_units(&units),
                Err(e) => messages.iter().find(|(k, _)| *k == e).map(|(_, m)| m.clone()).unwrap_or_default(),
            };

            if !mode.is_aes() {
                ui.set_output_text(describe(engine::transform(mode, &input, key, &keyword)).into());
                return;
            }
            ui.set_busy(true);
            ui.set_output_text("".into());
            let weak = ui.as_weak();
            std::thread::spawn(move || {
                let text = describe(engine::transform(mode, &input, key, &keyword));
                let _ = weak.upgrade_in_event_loop(move |ui| {
                    ui.set_output_text(text.into());
                    ui.set_busy(false);
                });
            });
        }
    });

    // [복사]: 결과가 비어 있으면 "LOL" (정식 버전과 같은 이스터 에그)
    ui.on_copy({
        let weak = ui.as_weak();
        let state = state.clone();
        move || {
            let ui = weak.unwrap();
            let output = ui.get_output_text();
            if output.is_empty() {
                ui.set_output_text("LOL".into());
                return;
            }
            if arboard::Clipboard::new().and_then(|mut c| c.set_text(output.to_string())).is_ok() {
                show_feedback(&ui, state.borrow().texts.get("COPY_FEEDBACK"));
            }
        }
    });

    ui.on_clear({
        let weak = ui.as_weak();
        move || {
            let ui = weak.unwrap();
            ui.set_input_text("".into());
            ui.set_output_text("".into());
        }
    });

    // 불러오기: UTF-8 텍스트만 (앞의 BOM 은 지움, 깨진 글자로 읽지 않음) — 정식 버전과 같은 규칙
    ui.on_load({
        let weak = ui.as_weak();
        let state = state.clone();
        move || {
            let ui = weak.unwrap();
            let Some(file) = rfd::FileDialog::new().pick_file() else { return };
            let st = state.borrow();
            match std::fs::read(&file) {
                Ok(bytes) => {
                    let body = bytes.strip_prefix(&[0xEF, 0xBB, 0xBF]).unwrap_or(&bytes);
                    match std::str::from_utf8(body) {
                        Ok(text) => {
                            ui.set_input_text(text.into());
                            ui.set_output_text("".into());
                        }
                        Err(e) => error_box(&st.texts.get("LOAD_ERROR_TITLE"), st.texts.get("LOAD_ERROR_MESSAGE") + &e.to_string()),
                    }
                }
                Err(e) => error_box(&st.texts.get("LOAD_ERROR_TITLE"), st.texts.get("LOAD_ERROR_MESSAGE") + &e.to_string()),
            }
        }
    });

    // 저장: UTF-8 (BOM 없이)
    ui.on_save({
        let weak = ui.as_weak();
        let state = state.clone();
        move || {
            let ui = weak.unwrap();
            let Some(file) = rfd::FileDialog::new().set_file_name("output.txt").save_file() else { return };
            if let Err(e) = std::fs::write(&file, ui.get_output_text().as_bytes()) {
                let st = state.borrow();
                error_box(&st.texts.get("SAVE_ERROR_TITLE"), st.texts.get("SAVE_ERROR_MESSAGE") + &e.to_string());
            }
        }
    });

    ui.on_mode_changed({
        let weak = ui.as_weak();
        let state = state.clone();
        move |index| {
            let ui = weak.unwrap();
            let mut st = state.borrow_mut();
            st.settings.cipher_mode = index;
            st.save();
            update_parameter(&ui, &st);
        }
    });

    ui.on_number_changed({
        let weak = ui.as_weak();
        let state = state.clone();
        move |value| {
            let ui = weak.unwrap();
            let mut st = state.borrow_mut();
            match current_mode(&ui) {
                Mode::ScytaleEncrypt | Mode::ScytaleDecrypt => st.settings.scytale_diameter = value,
                Mode::HangulEncrypt | Mode::HangulDecrypt => st.settings.hangul_shift = value,
                _ => st.settings.caesar_shift = value,
            }
            st.save();
        }
    });

    ui.on_language_changed({
        let weak = ui.as_weak();
        let state = state.clone();
        move |index| {
            let ui = weak.unwrap();
            let language = Language::ALL[index.clamp(0, 3) as usize];
            let mut st = state.borrow_mut();
            st.settings.language = language.saved().into();
            st.texts = Texts::load(language);
            st.save();
            apply_language(&ui, &st);
        }
    });

    ui.on_dark_changed({
        let state = state.clone();
        move |dark| {
            let mut st = state.borrow_mut();
            st.settings.dark = dark;
            st.save();
        }
    });

    ui.on_check_update({
        let weak = ui.as_weak();
        move || start_update_check(&weak.unwrap(), true)
    });

    // 하루 한 번 조용히 확인 (창이 뜬 뒤)
    if update::now_ms().saturating_sub(state.borrow().settings.last_update_check) >= update::AUTO_CHECK_INTERVAL_MS {
        let weak = ui.as_weak();
        slint::Timer::single_shot(std::time::Duration::from_secs(2), move || {
            if let Some(ui) = weak.upgrade() {
                start_update_check(&ui, false);
            }
        });
    }

    ui.run()
}
