# Helpdesk Sample Adjusted to reproduce the bug with projections and reservation number

Marten 7 (atm of writing 7.3.0)
has a bug in upsert function that updates the async snapshot projection.

If you would run the only test in this project, 
that merely creates 2 events and expects async snapshot projection to catch up with the latest version,

it will fail with the following error:

```bash
Xunit.Sdk.XunitException
Expected property root.Aggregated.Category to be equivalent to IncidentCategory.Database {value: 3}, but found <null>.

```

if you look at the logs you would see that the expected call is made by the daemon
that should have updated the projection with the new value, but it didn't.

```sh
[dbug] [Helpdesk.Api.Core.Marten.MartenLogger] SQL Batch starting 214668f9-f45c-4e7e-9b9e-b752f3a95229 select helpdesk.mt_upsert_incidentdetailssnapshotasyncprojection($1, $2, $3, $4); (@p0, Jsonb, {"Id":"018e4f4f-7480-43c0-8d73-5f5d877520ac","Aggregated":{"Id":"018e4f4f-7480-43c0-8d73-5f5d877520ac","CustomerId":"4a8c1416-2907-4d4d-8581-7d2c5ac44141","Status":"Pending","Notes":[],"Category":"Database","Priority":null,"AgentId":null,"Version":1}}), (@p1, Varchar, Helpdesk.Api.Incidents.GetIncidentDetails.IncidentDetailsSnapshotAsyncProjection), (@p2, Uuid, 018e4f4f-7480-43c0-8d73-5f5d877520ac), (@p3, Integer, 2)

```

however if you pause the test before running this batch and run the query manually 

```sql
select * 
	FROM helpdesk.mt_doc_incidentdetailssnapshotasyncprojection;
```

you would see that the projection version is already set to 2. which means that the upsert call with version 2 is going to be ignored

if you debug the code and put a breakpoint into projection

```csharp
public IncidentDetailsSnapshotAsyncProjection(IncidentLogged logged)
    {
        Id = logged.IncidentId;
        Aggregated = new(logged.IncidentId, logged.CustomerId, IncidentStatus.Pending, Array.Empty<IncidentNote>());
    }
```

you would notice that the breakpoint
is hit twice despite the fact that there is only one incident creation event in the stream.

So something forces marten to build projection twice from a single event and 
produce 2 duplicate upsert calls with the same version number = 1

which then goes into the following upsert function code block

```sql
if revision = 1 then
  SELECT mt_version FROM helpdesk.mt_doc_incidentdetailssnapshotasyncprojection into current_version WHERE id = docId ;
  if current_version is not null then
    revision = current_version + 1;
  end if;
end if;
```
and sets the version to 2, which is wrong, as the very next update with version 2 is ignored and not applied


