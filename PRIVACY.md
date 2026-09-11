# Privacy Notice

Last updated: 2026-09-08

This notice describes what personal data the AGC Utils bot ("the bot") collects, why, who it is
shared with, and how long it is kept. It reflects the behaviour of the code in this repository. If
the code changes, this notice is expected to change with it.

## 1. Scope

The bot is a private bot. It runs on a single Discord server, discord.gg/animegamingcafe, and is not offered
to or usable by other servers. It also serves a web dashboard at `dashboard.animegamingcafe.de`.

Discord itself is a separate controller and processes your data under its own terms. This notice
covers only what the bot does on top of that. See Discord's own privacy policy for the platform
side.

## 2. Controller and contact

Controller: the operator of animegamingcafe.

The practical contact route for members is a support ticket on the server. Requests sent that way
reach the staff team. Also via email: support@animegamingcafe.de

## 3. What is collected

### 3.1 Activity metrics

Collected automatically for every member, with no opt-out. Its for User stats purpose and automating things

| Data | Detail |
|---|---|
| Message record | For every message sent by a non-bot account: your user ID, the message ID, the channel ID, and a timestamp. The message text is **not** stored. |
| Voice presence | While you are in a voice or stage channel, a sample is taken every 60 seconds recording your user ID, the channel ID, a timestamp, and whether you are muted or deafened. |

These records are used for server statistics, for the leveling system, and to evaluate role and
permission conditions such as "has sent at least N messages in the last N days".

A table for tracking game and rich-presence activity exists in the database schema but no code
writes to it at the currnet state

### 3.2 Leveling

| Data | Detail |
|---|---|
| Level state | Your user ID, current XP, current level, the timestamps of your last text and voice XP awards, and whether you want to be pinged on level up. |
| Rank card settings | If you customise your rank card on the dashboard: your chosen background image, stored as base64 image data in the database, plus bar colour, font, and box transparency. |
| Rejected images | If a background image you upload is rejected by the automated image check, the **full rejected image** is stored together with your user ID, the rejection reason, and a timestamp. |
| XP transfers | When staff move XP between accounts: source user, destination user, the staff member who did it, the amount, and a timestamp. |

### 3.3 Moderation

| Data | Detail |
|---|---|
| Warnings | Your user ID, the moderator's user ID, the date, the reason text written by the moderator, whether the warning is permanent, and a case ID. |
| Flags | The same fields, used for lower-severity notes and for expired warnings (see section 7). |
| Ban log | Your user ID, the moderator's user ID, the ban reason, and a timestamp. |
| Evidence images | Screenshots attached by staff to a warning or flag are downloaded from Discord and stored on the operator's own image host. The resulting link is written into the case reason. See section 6. |
| Extra permissions | Your user ID, the permission, its state, any expiry date, the staff member who granted it, and a free-text reason. |

Moderation reasons are free text written by staff. They can describe your conduct and may quote what
you said.

### 3.4 Support tickets

| Data | Detail |
|---|---|
| Ticket records | Ticket ID, the ticket owner's user ID, the ticket type, the channel ID, whether it is claimed and by which staff member, whether it is closed, the user IDs of everyone added to the ticket, and the transcript links. |
| Transcripts | When a ticket is closed, the **entire ticket channel is exported to an HTML file and stored for reference purpose**.|
| Notification settings | If you are staff: which ticket channels you subscribe to and in which mode. |


### 3.5 Web dashboard

Logging in uses Discord OAuth. The bot requests the `identify` and `guilds` scopes. It does **not**
request your email address, so no email address is collected.

| Data | Detail |
|---|---|
| Session | Your user ID, username, display name, discriminator, avatar hash, and your role on the server, held in your login session. |
| OAuth tokens | Your Discord access and refresh tokens are stored inside the encrypted authentication cookie in your browser. |
| Cookies | An authentication cookie and a session cookie with a 30 minute idle timeout. Both are HttpOnly and SameSite=Lax, and both are strictly necessary for the dashboard to work. There are no analytics or advertising cookies. |
| Staff applications | If you submit an application: an application ID, your user ID, the position, a timestamp, and your full free-text application. The application text is base64 encoded in the database, which is an encoding and not encryption. The user IDs of the staff who opened your application are also recorded. |


The dashboard loads its fonts, icons, stylesheets and scripts from its own server. The only images
fetched from elsewhere are Discord avatars, which your browser loads from Discord's image servers.

### 3.6 Other features

| Feature | Data |
|---|---|
| Counting game | Your user ID, your correct-count total, your failure count, your saves, and high score entries with timestamps. |
| Polls | Poll configuration and, for each vote, the poll ID, the chosen option, and **your user ID**. Note that this applies to polls marked as anonymous too. Anonymity is only applied when results are displayed; the vote is not anonymous in the database. |
| Temporary voice channels | The channel owner's user ID, your saved channel name, bitrate and user limit, your block list and permit list, and your lock and hide preferences. |
| Introduction cooldown | Your user ID and the time of your last post in the introduction channel. |

### 3.7 What is posted to Discord channels

Some processing results in your data being posted into staff-visible channels rather than stored in
the database:

- If you report a message, the reported message content, the reported user's details, and your own
  user ID and note are posted to the report channel and the staff chat.
- Automated  AutoMod alerts post the message content and the author's details to an alert
  channel.
- The anti-raid kick posts the affected username, user ID and account creation date to a log
  channel.
- Errors post the exception details
- Webhook for deleted or edited messages for moderation purposes


## 4. Legal bases

| Purpose | Legal basis |
|---|---|
| Moderation, safety, anti-raid, abuse prevention, ban and warning records, evidence retention | Art. 6(1)(f) GDPR, legitimate interests: keeping the server safe and enforcing its rules. Without a durable moderation record the moderation system cannot function. |
| Activity metrics used for moderation context and for role and permission conditions | Art. 6(1)(f) GDPR, legitimate interests. |
| Support tickets and their transcripts | Art. 6(1)(b) GDPR where the ticket concerns your participation on the server, otherwise Art. 6(1)(f). |
| Leveling, rank cards, counting, polls, temporary voice channels, staff applications | Art. 6(1)(a) GDPR, consent, given by choosing to use the feature. You can stop using these features at any time. |
| Dashboard authentication and session cookies | Art. 6(1)(b) GDPR, necessary to provide the service you requested by logging in. |
| Error reporting | Art. 6(1)(f) GDPR, legitimate interests: keeping the bot working. |

Where processing rests on legitimate interests, you have the right to object. See section 8.

## 5. Recipients and third parties

| Recipient | What is sent | When |
|---|---|---|
| Discord | Everything, since the bot operates on the Discord platform | Always |
| Server staff | Moderation records, tickets and transcripts, applications, member lists, ban and XP logs | Through the bot and the dashboard |
| Transcript host and image host | See section 6 | On ticket close and on moderation actions with attachments |

The transcript host and the image host are operated by the server operator, not by an outside
company.

The bot is not used to sell data, and there is no advertising or profiling for marketing purposes. Also there is no collecting without a purpose.

## 6. Publicly hosted content

Two categories of data are placed on the public internet. This is the most sensitive processing the
bot performs and it deserves to be stated plainly.

**Ticket transcripts.** When a ticket is closed, the whole channel including every attachment is
exported to an HTML file and published at `https://ticketsystem.animegamingcafe.de/`.
Directory listing is disabled, so the file can only be found by knowing its cryptic address, which is the access token itself. Anyone who has the link, or who obtains it later, can read the entire ticket. The link is sent to the ticket owner by direct message and posted in the staff log channel.

If you are uncomfortable with this, keep it in mind when sharing sensitive material in a ticket.

## 7. Retention

The bot has **no automated deletion routine** for personal data as they are needed for the moderation. Records are kept until they are
removed manually. Specifically:

| Data | Retention |
|---|---|
| Warnings | A non-permanent warning leaves your active warning list after a configurable period, by default 7 days. It is **not deleted**. It is converted into a flag, keeping the original reason text and prefixing it, and retained indefinitely. Permanent warnings stay on the active list. |
| Flags, ban log, XP transfer log | Indefinite |
| Message and voice metrics, command records | Indefinite |
| Ticket records, transcripts, and re-hosted attachments | Indefinite |
| Staff applications | Indefinite |
| Rejected rank card images | Indefinite |
| Leveling data, counting stats, temp voice settings | Kept while you are a member and beyond, until removed manually |
| Server log files | Daily rolling files with no configured retention limit |
| Dashboard session | 30 minutes of inactivity |
| In-memory caches | Up to 10,000 recent messages with their content for deletion tracking purposes, the full member list refreshed every 30 minutes, and every ticket transcript. All are lost when the bot restarts. |

Warning notification channels created for you are deleted after 24 hours.

## 8. Your rights

Under the GDPR you have the right to:

- request access to the personal data held about you
- request rectification of inaccurate data
- request erasure, subject to the limits described in section 9
- request restriction of processing
- object to processing based on legitimate interests
- request your data in a portable form
- lodge a complaint with your national data protection supervisory authority

There is no automated data export or erasure feature in the bot. Every request is handled manually
by the staff team, so allow reasonable time for a response.

## 9. Requesting deletion

To request access to or deletion of your data, open a support ticket on the server, or use the
contact address in section 2 if you can no longer access the server.

Please read the following before requesting deletion.

**Deletion means leaving permanently.** The moderation records are mandatory for the moderation
system to work. They are what lets staff see whether an account has prior warnings, flags or bans. If
your data is deleted while you remain a member, or if you rejoin afterwards, the bot will simply
start recording again from scratch and the moderation history that protects the server is lost. For
that reason, a deletion request is handled on the basis that **you will not rejoin the server**. If
you rejoin, new records will be created from that point and the request is effectively undone.

**Some records cannot be erased on request.** Where there is an overriding legitimate interest in
keeping them, in particular:

- records connected to an active ban
- records connected to an open moderation case or an ongoing investigation
- records needed to defend against a legal claim

these are retained until that interest no longer applies. You will be told which records are
affected and why. Everything not covered by this can be removed.

**Content posted by others is out of scope.** A ticket transcript or a report contains messages
written by other people as well as by you. Where a record cannot be separated cleanly, staff will
explain what can and cannot be removed.

## 10. Changes to this notice

This notice is kept in the bot's source repository. Changes are visible in the repository's commit
history. The date at the top reflects the last substantive update.
