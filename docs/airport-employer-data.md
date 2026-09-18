# Airport and employer data pipeline

OpenCareer should use real airport/carrier activity where reliable public data exists, but keep the **career simulation** distinct from claims about a company's actual hiring, contracts or military tasking.

## Source priority

### U.S. airport scale

Use FAA passenger enplanement and all-cargo data to calibrate:

- airport market scale,
- passenger intensity,
- cargo intensity,
- major/medium/small/non-hub classifications where applicable.

The FAA publishes annual passenger boarding and all-cargo data and explains that its Air Carrier Activity Information System uses DOT/BTS T-100 data plus additional airport activity reporting.

### Carrier + route presence

Use BTS T-100 segment/market data for:

- carrier identity,
- origin/destination presence,
- passengers,
- freight and mail,
- scheduled/performed departures,
- available capacity,
- aircraft type/service class,
- aircraft hours/load factor where available.

This is a better basis for airline/cargo presence than manually hardcoding assumptions.

### Local/specialty operators

For activity that T-100 does not represent well, prefer airport/operator/government primary sources:

- FBOs and local charter operators,
- skydiving businesses,
- flight schools,
- medical/public-service aviation,
- UAS/research centers,
- military/government presence,
- special cargo or industrial operators.

Store the source and observation date with every curated specialty record.

## Proposed normalized records

```text
AirportMarketData
  ICAO
  SnapshotYear
  PassengerEnplanements
  CargoLandedWeight
  CommercialDepartures
  AirportMarketScale
  SourceReferences[]

AirportCarrierPresence
  ICAO
  CarrierId
  CarrierDisplayName
  PassengerActivityIndex
  CargoActivityIndex
  DepartureActivityIndex
  SnapshotPeriod
  SourceReference

AirportSpecialtyPresence
  ICAO
  Specialty
  OperatorName?
  Strength
  ValidFrom
  ValidTo?
  SourceReference
  SourcePublisher
```

The domain should consume normalized indexes rather than raw CSV columns.

## Employer generation

Real carrier presence may create an OpenCareer `EmployerOperatingProfile`, but game contracts are simulated.

Examples:

- strong passenger presence -> more employee/airline passenger opportunities;
- strong freight presence -> more cargo/express jobs;
- strong local charter operator -> charter/ferry/reposition opportunities;
- military/government presence -> authorized military/government jobs when the player has the required career access;
- skydiving operator -> local jump operations rather than generic long-distance cargo.

Repeated player work then builds **OpenCareer employer trust** independently of real-world loyalty programs, employment status or hiring practices.

## Real-company presentation

If real company names are enabled:

- use text names primarily; do not assume rights to logos/liveries/brand artwork;
- describe jobs as simulated OpenCareer opportunities, not actual offers from the company;
- never imply endorsement or partnership;
- keep a fictional-employer fallback mode so core gameplay does not depend on internet datasets or branding permissions;
- before public commercial release, review trademark/data-licensing requirements for any redistributed data pack.

## Refresh cadence

Do not download large traffic datasets every time the app starts.

Preferred approach:

1. ship/version a normalized airport/employer snapshot with the app;
2. optionally update the data pack on a slow cadence (monthly/quarterly/annual depending on source);
3. use deterministic daily/hourly game simulation on top of that baseline;
4. use the optional live-world signal feed only for short-lived trends/events.

This keeps startup fast and the career playable offline.

## U.S. first, extensible globally

The first data pipeline can be U.S.-specific because FAA/BTS sources are strong and KRME is the test hub. Keep source adapters behind interfaces so other countries can later use their own aviation authorities/statistical sources without changing Domain models.
