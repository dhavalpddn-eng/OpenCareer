# OpenCareer KJFK live-test runbook

This is the shortest supported interactive test of the playable career loop. It runs OpenCareer in an isolated Development/Test profile and does not open, reset, or modify the normal career database.

## 1. Prerequisites

- Windows x64.
- Microsoft Flight Simulator 2024 and the stock Cessna 172.
- The .NET 10 SDK available as `dotnet` in PowerShell.
- `SimConnect.dll` from the MSFS 2024 SDK.
- No existing `OpenCareer.App` process. Close every OpenCareer window before launching or resetting the test profile.

If `MSFS2024_SDK` is configured, the launcher finds `SimConnect.dll` automatically. Otherwise, locate it under the SDK's `SimConnect SDK\lib` directory.

## 2. Start a clean isolated run

Open PowerShell in the repository root and run:

```powershell
.\tools\run-kjfk-live-test.ps1 -SimConnectNativePath "C:\path\to\SimConnect.dll"
```

The default is **Clean Reset**. It clears only `%LOCALAPPDATA%\OpenCareer.LiveTests\KJFK`, after validating the test-profile marker. It does not touch normal OpenCareer data.

If `MSFS2024_SDK` is already configured, use:

```powershell
.\tools\run-kjfk-live-test.ps1
```

Leave that PowerShell window open. It builds and launches OpenCareer and finalizes the diagnostic package after the app closes.

## 3. Prepare MSFS

1. Start MSFS 2024.
2. Load the stock Cessna 172 at KJFK on a parking stand or ramp position.
3. Use a cold-and-dark state: aircraft on the ground, parking brake set, and every engine off.
4. Make sure the simulator is not paused and Slew mode is off.
5. Leave MSFS running while using OpenCareer.

OpenCareer must observe the aircraft in the simulator. Seeing the C172 in an installation list alone is not enough for the live flight.

## 4. Create the test flight

1. Open **Career / Profile**. If onboarding is shown, enter `KJFK` and select **Start Career**.
2. Open **Jobs**.
3. Wait for the current station and installed-aircraft status to finish loading. If the page reports a failure, select **Refresh Jobs** once.
4. Select the stock C172 in **Assigned Aircraft**.
5. In **Zero-Pay Calibration**, optionally select **Move pilot to KJFK for this test**, then select **Create Zero-Pay Calibration**.
6. Find the KJFK local-circuit offer and select its action button to start the flight.
7. Confirm that OpenCareer navigates to **Current Flight** and the live checklist begins advancing.

The calibration flight intentionally displays **$0 PAY · $0 PROGRESSION**. It tests telemetry, landing, shutdown, settlement plumbing, and Logbook creation. It is not a paid contract.

## 5. Fly and complete

1. Start the engine only after Current Flight is open.
2. Taxi, take off, and fly a local circuit.
3. Land full stop. A small bounce is supported, but do not use Slew, pause, or teleport the aircraft.
4. Taxi clear, park, set the parking brake, and shut down every engine.
5. Wait for the checklist to show the shutdown evidence. If completion is disabled, read the reason directly above **Complete Career Flight**; it names the missing evidence.
6. In the first **Flight Actions** card, select **Complete Career Flight**.

Expected completion receipt:

- `EVALUATION COMPLETE`
- `$0 calibration pay (by design)`
- the post-settlement balance
- `KJFK → KJFK logged`
- pilot location `KJFK`
- the career-flight number

Then open **Logbook** and verify one new automatic career entry. Open **Career / Profile** to verify the profile and current airport remain visible.

## 6. Test cancellation

For an active or interrupted flight, open **Current Flight**. **Abandon Current Flight** is in the first Flight Actions card. Confirming it must cancel the contract, release the aircraft reservation, clear the saved session, and award no pay or completion credit.

Normal paid jobs no longer require the manual Dispatch workspace. Acceptance applies the standard civilian planning policy, runs the authoritative aircraft/fuel/runway evaluation, persists the exact feasible plan and starts the flight. If automatic preparation fails after acceptance, OpenCareer cancels the contract and releases its reservation before reporting the error. The Dispatch cancellation control remains only as recovery for state created by an older build.

## 7. Test restart recovery

1. Start another calibration flight.
2. Close OpenCareer after the flight has started but before completion.
3. Launch the same isolated profile without resetting it:

```powershell
.\tools\run-kjfk-live-test.ps1 -PreserveState -SimConnectNativePath "C:\path\to\SimConnect.dll"
```

4. Reopen **Current Flight** and verify that the saved session is recovered.
5. Either finish it normally or use **Abandon Current Flight** to verify cleanup.

Do not use `-PreserveState` for the first run after an unknown failure. Start with a clean reset so stale evidence cannot contaminate the result.

## 8. Test a paid contract

Use code checkpoint `714a048b` or later. This is the remaining ordinary paid-job live acceptance gate; automated tests do not satisfy it.

1. Start from a clean isolated profile and record the balance shown on **Career / Profile**.
2. Do not use the calibration card. On **Jobs**, choose an **Available Contract** whose card shows a nonzero quoted payment and a route supported by the loaded aircraft.
3. Select **Accept & Start Flight** once. OpenCareer must prepare the plan automatically, open **Current Flight**, retain the exact aircraft reservation and never require **Advanced Dispatch** or another profile setup.
4. Before engine start, close OpenCareer and relaunch with `-PreserveState`. **Current Flight** must recover the same contract, route and FlightSession without accepting it again.
5. Start, taxi and take off only after the checklist is live. In stable flight, close OpenCareer while MSFS remains running, then relaunch immediately with `-PreserveState`. The same session must recover; no second contract or payment may appear.
6. Land at the contracted destination, park, set the parking brake and shut down every engine. Wait for the established takeoff, airborne, landing, parking and shutdown evidence before selecting **Complete Career Flight**.
7. The receipt must show all of the following authoritative values:
   - `EVALUATION COMPLETE`
   - `Gross` with the nonzero cash receipt
   - `Operating costs` with the player-paid amount, including `$0` when the employer covered them
   - `Net` equal to gross minus player operating costs
   - the resulting `Balance`
   - the contracted origin and destination logged
   - pilot location equal to the contracted destination
8. Open **Logbook** and verify exactly one new automatic career entry with the same settlement. Return to **Jobs** and verify the aircraft is no longer trapped by the completed reservation.
9. Close OpenCareer after completion and relaunch with `-PreserveState`. Verify the balance, Logbook count and destination are unchanged. No completion action, duplicate ledger entry or second payment may appear.

Failure evidence to retain:

- a paid contract that opens Advanced Dispatch or requests repeated profile setup;
- a missing or replaced contract/session after either restart;
- completion enabled without the required simulator evidence;
- calibration wording, unexplained zero gross receipt or arithmetic that does not satisfy `net = gross - operating costs`;
- a changed balance, extra Logbook entry, duplicate payment or retained aircraft reservation after the final restart;
- a raw persistence/settlement exception without a retryable **Complete Career Flight** recovery action.

## 9. Collect the result

Close OpenCareer normally. The launcher prints the exact paths to:

- `run-result.json`
- the diagnostic ZIP

They are stored under `%LOCALAPPDATA%\OpenCareer.LiveTests\KJFK\Diagnostics\KjfkLiveTest`. Keep the PowerShell error text and the newest diagnostic ZIP when reporting a failure.
