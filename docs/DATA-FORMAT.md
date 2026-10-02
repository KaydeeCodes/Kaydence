# How Kaydence stores your diary

Kaydence keeps everything as ordinary files in a documented format, so your diary is never locked in. This page is for anyone curious about the details, or who wants to read their diary with other tools. For a ready made copy you can open anywhere, use Settings, Your diary folder, Export to a folder.

## Folder layout

```
%LocalAppData%\Kaydence\
  settings.json
  Logs\
    kaydence-2026-09-30.log (one per day, last 14 kept)
  Backups\
    Kaydence-2026-09-30.zip (one per day, last 14 kept)
  Diary\
    index.json
    medications.json
    counters.json
    keys.json               (only when a password is on)
    entries\
      2026\
        2026-09-30\
          day.json
          ink.isf
          img-143012-a1b2c3.jpg
          voice-143512-d4e5f6.wav
```

## day.json

```json
{
  "Version": 1,
  "Date": "2026-09-30",
  "CheckIn": {
    "Mood": 4,
    "MoodNote": "Tired but okay",
    "Medications": [
      { "MedId": "5f0c...", "Name": "Estradiol", "Dose": "2mg", "Time": "08:15" }
    ],
    "Transition": { "Hrt": "Estradiol gel, 2 pumps", "IsMilestone": false },
    "Symptoms": { "Pain": 3, "Areas": { "lower-back": 3, "knee-left": 1 } },
    "Sleep": { "Hours": 7.5 },
    "Fitness": { "Minutes": 30 },
    "BloodPressure": [ { "Time": "09:00", "Systolic": 118, "Diastolic": 76, "Pulse": 64 } ],
    "WeightKg": 68.4,
    "Cycle": { "Flow": "Medium", "Symptoms": "Cramps, Tired" },
    "Habits": { "Water": 6, "Caffeine": 2, "Smoked": false, "Drank": true, "Alcohol": 1.5 },
    "Tasks": [ { "Text": "Book blood test", "Done": false } ]
  },
  "Items": [
    { "Kind": "text", "X": 48, "Y": 40, "Width": 520, "Z": 0, "Xaml": "<Section ...>" },
    { "Kind": "image", "X": 90, "Y": 220, "Width": 420, "Height": 280, "Z": 1, "Image": "img-143012-a1b2c3.jpg" },
    { "Kind": "sticky", "X": 600, "Y": 40, "Width": 220, "Z": 2, "Color": "#FFF1A8", "Xaml": "<Section ...>" },
    { "Kind": "voice", "X": 48, "Y": 620, "Width": 330, "Z": 4, "Audio": "voice-143512-d4e5f6.wav", "Duration": 42.5, "Wave": "base64 of 48 bar heights" },
    { "Kind": "checklist", "X": 48, "Y": 540, "Width": 300, "Z": 3, "Checks": [ { "Text": "Ring the GP", "Done": true } ] }
  ],
  "Text": "A really lovely Wednesday...",
  "Updated": "2026-09-30T14:31:02"
}
```

- **Mood** is 1 to 5, where 5 is Great and 1 is Awful.
- Empty values are left out of the file.
- **Items** are the things on the freeform page. Text boxes and sticky notes keep their formatting as WPF FlowDocument XAML. Sticky notes also keep their paper colour. Checklists keep each line and whether it's ticked. Pictures point to a file in the same day folder.
- Photos added from files are saved as a clean copy: turned upright, with location, camera and date details left out. JPEG photos stay JPEG, other formats become PNG, and GIFs and BMPs are kept exactly as they are.
- Rotating or cropping a picture saves a brand new picture file and points the item at it. The old file is tidied away when you leave the day.
- **Z** is the stacking order, so higher numbers sit on top.
- **Text** is a plain copy of everything written on the page, used for search, snippets and printing.
- **WeightKg** is always stored in kg, whatever unit is shown.
- **Areas** in Symptoms says where it hurts: each key is a body area and the value is 1 for mild, 2 for moderate or 3 for severe. Left and right always mean the person's own left and right. The keys are `head`, `neck`, `chest`, `stomach`, `pelvis`, `upper-back`, `middle-back`, `lower-back`, `buttocks`, and `shoulder`, `upper-arm`, `forearm`, `hand`, `thigh`, `knee`, `lower-leg` and `foot`, each followed by `-left` or `-right`.

## ink.isf

This holds pen, highlighter and shape drawings in Windows Ink Serialized Format. It only exists if something was drawn that day.

## index.json

A tiny summary per day that the calendar uses for dots. If it's deleted or damaged, Kaydence rebuilds it from the day files on the next start.

```json
{ "2026-09-30": { "Mood": 4, "Milestone": false, "Flow": "Medium" } }
```

## counters.json

The days since counters. `Since` is the start date, and a date in the future counts down instead.

```json
[ { "Id": "9a1c...", "Name": "Started HRT", "Since": "2025-03-14" } ]
```

## medications.json

The saved medication list. Removing a medication only hides it (`"Active": false`) so old days still show it.

## Encryption

When the password is on, every file in the Diary folder except `keys.json` is sealed:

```
"KDE1" (4 bytes) | nonce (12 bytes) | tag (16 bytes) | AES-256-GCM ciphertext
```

- One random 256 bit diary key seals every file. It is never stored as it is.
- `keys.json` holds that key wrapped twice with AES-GCM: once with a key made from the password (PBKDF2, SHA-256, 300,000 rounds) and once with a key made from the random secret in the recovery file.
- Changing the password only rewraps the key, so the files don't need sealing again.
- In "only ask after a while" mode, `settings.json` also keeps a copy of the key protected by Windows (DPAPI) for your Windows account on that PC.
- A file without the `KDE1` start is plain, so Kaydence can finish an interrupted encrypt or decrypt next time it opens.
- Backups are zips of the sealed files plus `keys.json`, so they need the password or recovery file from when they were made.

## Recovery file

A `.kaydencekey` file is a small JSON file holding a random 256 bit secret, written as base64 in its `Key` field. Kaydence reads the `Key` field when the file is chosen on the lock screen.

## Logs

`Logs\kaydence-YYYY-MM-DD.log` records what the app did, so a bug report can say exactly where things went wrong.

- Every session starts with a header: app version, build, .NET runtime, Windows version, 64 bit, processors, memory, culture, time zone, screen size and graphics tier.
- Each line has the date, time with milliseconds and UTC offset, level, thread and area, like `2026-09-30 21:14:05.123 +01:00 INFO  [ 1] [Pictures] Loaded img-211405-a1b2c3.png at 1920 x 1080`.
- It notes things like which day was opened, how many items of each kind a page has, file names, sizes and whether they're encrypted, how long saves took, settings that changed, and every error in full.
- It never records what you wrote, your check-in answers, passwords, keys, search words or clipboard contents. Your Windows user name is replaced with `<user>` in any file path.
- Logs are kept for 14 days and each day's log stops growing at 20 MB.
- Settings, Help and feedback, Make a bug report zips the last three days of logs with an `about.txt` holding the Kaydence and Windows versions, the space used, and the settings, where passwords, keys and hashes only ever show as `(set)` or `(none)`.

## Safety

- Every save writes a temporary file first and then swaps it in, so a crash can never leave half a file.
- If a day file is ever damaged, Kaydence keeps a copy named `day.json.broken-<time>` before carrying on.
- A day that is completely cleared has its folder tidied away.
- Pictures deleted from a page stay in the day folder until you move to another day or close Kaydence, so undo can bring them back.
- Deleting a day from the calendar moves its whole folder into `Diary\deleted\`, it's never wiped.
