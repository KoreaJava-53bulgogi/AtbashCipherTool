//! 고전 암호 (C# 의 ClassicCiphers.cs = Java 버전과 같은 결과). 모두 UTF-16 단위로 처리.

use super::java;

const GA: u16 = 0xAC00; // '가'
const HIH: u16 = 0xD7A3; // '힣'
const HANGUL_COUNT: i32 = 11172;

fn ch(c: char) -> u16 {
    c as u16
}

/// 아트배쉬: 알파벳·숫자·한글 음절·특수문자 묶음 안에서 순서를 뒤집음
pub fn atbash(text: &[u16]) -> Vec<u16> {
    let ranges = [
        (ch('a'), ch('z')),
        (ch('A'), ch('Z')),
        (GA, HIH),
        (ch('0'), ch('9')),
        (ch('!'), ch('/')),
        (ch(':'), ch('@')),
        (ch('['), ch('`')),
        (ch('{'), ch('~')),
    ];
    text.iter()
        .map(|&c| match ranges.iter().find(|(lo, hi)| (*lo..=*hi).contains(&c)) {
            Some(&(lo, hi)) => hi - c + lo,
            None => c,
        })
        .collect()
}

/// Java/C# 의 int 계산을 그대로 (넘치면 감싸 돌기)
fn shift_char(c: u16, base: u16, range: i32, shift: i32) -> u16 {
    let mut pos = (c as i32 - base as i32).wrapping_add(shift) % range;
    if pos < 0 {
        pos += range;
    }
    (base as i32 + pos) as u16
}

/// 카이사르: 영문(26), 숫자(10), 한글 음절(11172) 안에서 이동. 복호화는 `shift` 에 음수(-shift)
pub fn caesar(text: &[u16], shift: i32) -> Vec<u16> {
    text.iter()
        .map(|&c| match c {
            0x61..=0x7A => shift_char(c, ch('a'), 26, shift),
            0x41..=0x5A => shift_char(c, ch('A'), 26, shift),
            0x30..=0x39 => shift_char(c, ch('0'), 10, shift),
            GA..=HIH => shift_char(c, GA, HANGUL_COUNT, shift),
            _ => c,
        })
        .collect()
}

pub fn caesar_encrypt(text: &[u16], shift: i32) -> Vec<u16> {
    caesar(text, shift)
}

pub fn caesar_decrypt(text: &[u16], shift: i32) -> Vec<u16> {
    caesar(text, shift.wrapping_neg())
}

/// 스퀴탈레 암호화: 공백을 지운 뒤 지름(열 수)만큼의 표에 세로로 쓰고 가로로 읽음. 빈칸은 'X'
pub fn scytale_encrypt(text: &[u16], diameter: i32) -> Vec<u16> {
    if text.is_empty() || diameter <= 0 {
        return text.to_vec();
    }
    let clean: Vec<u16> = text.iter().copied().filter(|&c| !java::is_regex_space(c)).collect();
    if clean.is_empty() {
        return Vec::new();
    }
    let d = diameter as usize;
    let len = clean.len();
    let rows = len.div_ceil(d);
    let mut grid = vec![0u16; rows * d];
    let mut k = 0;
    for col in 0..d {
        for row in 0..rows {
            grid[row * d + col] = if k < len {
                k += 1;
                clean[k - 1]
            } else {
                ch('X')
            };
        }
    }
    grid
}

/// 스퀴탈레 복호화. (Java 와 같이 '\0' 글자는 빈칸으로 보고 버림)
pub fn scytale_decrypt(text: &[u16], diameter: i32) -> Vec<u16> {
    if text.is_empty() || diameter <= 0 {
        return text.to_vec();
    }
    let d = diameter as usize;
    let len = text.len();
    let rows = len.div_ceil(d);
    let mut grid = vec![0u16; rows * d];
    grid[..len].copy_from_slice(text);
    let mut out = Vec::with_capacity(len);
    for col in 0..d {
        for row in 0..rows {
            let c = grid[row * d + col];
            if c != 0 {
                out.push(c);
            }
        }
    }
    out
}

/// 비즈네르 (Java 8.1.1 과 같은 결과): 영문자는 0~25칸, 한글 음절은 키워드 글자 코드값만큼, 그 밖은 그대로
pub fn vigenere(text: &[u16], key: &[u16], encrypt: bool) -> Vec<u16> {
    if key.is_empty() {
        return text.to_vec();
    }
    let key = java::to_upper(key);
    let mut key_index = 0usize;
    let mut out = Vec::with_capacity(text.len());
    for &c in text {
        let upper = (0x41..=0x5A).contains(&c);
        let lower = (0x61..=0x7A).contains(&c);
        if upper || lower {
            let base = if upper { ch('A') } else { ch('a') } as i32;
            let k = key[key_index % key.len()] as i32;
            let mut shift = ((k - 0x41) % 26 + 26) % 26;
            if !encrypt {
                shift = (26 - shift) % 26;
            }
            out.push((base + (c as i32 - base + shift) % 26) as u16);
            key_index += 1;
        } else if (GA..=HIH).contains(&c) {
            let mut shift = key[key_index % key.len()] as i32 % HANGUL_COUNT;
            if !encrypt {
                shift = (HANGUL_COUNT - shift) % HANGUL_COUNT;
            }
            out.push((GA as i32 + (c as i32 - GA as i32 + shift) % HANGUL_COUNT) as u16);
            key_index += 1;
        } else {
            out.push(c);
        }
    }
    out
}

const MORSE: [(char, &str); 39] = [
    ('A', ".-"), ('B', "-..."), ('C', "-.-."), ('D', "-.."), ('E', "."), ('F', "..-."),
    ('G', "--."), ('H', "...."), ('I', ".."), ('J', ".---"), ('K', "-.-"), ('L', ".-.."),
    ('M', "--"), ('N', "-."), ('O', "---"), ('P', ".--."), ('Q', "--.-"), ('R', ".-."),
    ('S', "..."), ('T', "-"), ('U', "..-"), ('V', "...-"), ('W', ".--"), ('X', "-..-"),
    ('Y', "-.--"), ('Z', "--.."),
    ('1', ".----"), ('2', "..---"), ('3', "...--"), ('4', "....-"), ('5', "....."),
    ('6', "-...."), ('7', "--..."), ('8', "---.."), ('9', "----."), ('0', "-----"),
    ('.', ".-.-.-"), (',', "--..--"), ('?', "..--.."),
];

fn units(s: &str) -> Vec<u16> {
    s.encode_utf16().collect()
}

/// 모스 부호로: 글자 사이는 공백, 단어 사이는 " / ". 표에 없는 글자는 버림
pub fn to_morse(text: &[u16]) -> Vec<u16> {
    if text.is_empty() {
        return Vec::new();
    }
    let words = java::split_spaces(&java::to_upper(text));
    let encoded: Vec<String> = words
        .iter()
        .map(|word| {
            word.iter()
                .filter_map(|&c| MORSE.iter().find(|(m, _)| *m as u16 == c).map(|(_, code)| *code))
                .collect::<Vec<_>>()
                .join(" ")
        })
        .collect();
    units(&encoded.join(" / "))
}

/// 모스 부호에서: 모르는 부호는 '?'
pub fn from_morse(morse: &[u16]) -> Vec<u16> {
    if morse.is_empty() {
        return Vec::new();
    }
    let words = java::split_literal(java::trim(morse), &units(" / "));
    let mut out = Vec::new();
    for (i, word) in words.iter().enumerate() {
        if i > 0 {
            out.push(ch(' '));
        }
        for code in java::split_literal(java::trim(word), &units(" ")) {
            let found = MORSE.iter().find(|(_, m)| units(m) == code);
            out.push(found.map_or(ch('?'), |(c, _)| *c as u16));
        }
    }
    out
}

/// 점, 줄, 슬래시, 공백으로만 이루어져 있으면 모스 부호로 봄
pub fn is_morse(text: &[u16]) -> bool {
    let t = java::trim(text);
    !t.is_empty() && t.iter().all(|&c| c == ch('.') || c == ch('-') || c == ch('/') || java::is_regex_space(c))
}

/// 이진수로: UTF-8 바이트를 8자리씩 공백으로 구분
pub fn to_binary(text: &[u16]) -> Vec<u16> {
    if text.is_empty() {
        return Vec::new();
    }
    let parts: Vec<String> = java::utf8_bytes(text).iter().map(|b| format!("{b:08b}")).collect();
    units(&parts.join(" "))
}

/// 이진수에서. 잘못된 조각이 있으면 "INVALID BINARY FORMAT"
pub fn from_binary(binary: &[u16]) -> Vec<u16> {
    if binary.is_empty() {
        return Vec::new();
    }
    let mut bytes = Vec::new();
    for part in java::split_spaces(java::trim(binary)) {
        match java::parse_int(&part, 2) {
            Some(v) => bytes.push(v as u8),
            None => return units("INVALID BINARY FORMAT"),
        }
    }
    java::utf8_string(&bytes)
}

pub fn is_binary(text: &[u16]) -> bool {
    let t = java::trim(text);
    !t.is_empty() && t.iter().all(|&c| c == ch('0') || c == ch('1') || java::is_regex_space(c))
}

/// Java `Math.floorMod` (int 범위에서 넘침 없이)
fn floor_mod(x: i32, m: i32) -> i32 {
    ((x as i64 % m as i64 + m as i64) % m as i64) as i32
}

/// 한글 자모 암호: 음절을 초성·중성·종성으로 나눠 각각 이동 (받침 유무는 그대로). 복호화는 -shift
pub fn hangul(text: &[u16], shift: i32) -> Vec<u16> {
    const CHO: i32 = 19;
    const JUNG: i32 = 21;
    const JONG: i32 = 28;
    text.iter()
        .map(|&c| {
            if !(GA..=HIH).contains(&c) {
                return c;
            }
            let offset = c as i32 - GA as i32;
            let mut cho = offset / (JUNG * JONG);
            let mut jung = offset % (JUNG * JONG) / JONG;
            let mut jong = offset % JONG;
            cho = floor_mod(cho.wrapping_add(shift), CHO);
            jung = floor_mod(jung.wrapping_add(shift), JUNG);
            if jong != 0 {
                jong = floor_mod((jong - 1).wrapping_add(shift), JONG - 1) + 1;
            }
            (GA as i32 + (cho * JUNG + jung) * JONG + jong) as u16
        })
        .collect()
}

pub fn hangul_encrypt(text: &[u16], shift: i32) -> Vec<u16> {
    hangul(text, shift)
}

pub fn hangul_decrypt(text: &[u16], shift: i32) -> Vec<u16> {
    hangul(text, shift.wrapping_neg())
}
