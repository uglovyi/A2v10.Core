/*
Copyright © 2008-2023 Oleksandr Kukhtin

Last updated : 26 may 2023
module version : 8100
*/
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.SCHEMATA where SCHEMA_NAME=N'a2sys')
	exec sp_executesql N'create schema a2sys';
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.SCHEMATA where SCHEMA_NAME=N'a2security')
	exec sp_executesql N'create schema a2security';
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.SCHEMATA where SCHEMA_NAME=N'a2ui')
	exec sp_executesql N'create schema a2ui';
go
------------------------------------------------
grant execute on schema ::a2sys to public;
grant execute on schema ::a2security to public;
grant execute on schema ::a2ui to public;
go

------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2sys' and TABLE_NAME=N'SysParams')
create table a2sys.SysParams
(
	Name sysname not null constraint PK_SysParams primary key,
	StringValue nvarchar(255) null,
	IntValue int null,
	DateValue datetime null,
	GuidValue uniqueidentifier null
);
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.SEQUENCES where SEQUENCE_SCHEMA=N'a2security' and SEQUENCE_NAME=N'SQ_Tenants')
	create sequence a2security.SQ_Tenants as bigint start with 100 increment by 1;
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2security' and TABLE_NAME=N'Tenants')
create table a2security.Tenants
(
	Id	int not null constraint PK_Tenants primary key
		constraint DF_Tenants_PK default(next value for a2security.SQ_Tenants),
	[Admin] bigint null, -- admin user ID
	[Source] nvarchar(255) null,
	[TransactionCount] bigint not null constraint DF_Tenants_TransactionCount default(0),
	LastTransactionDate datetime null,
	UtcDateCreated datetime not null constraint DF_Tenants_UtcDateCreated default(getutcdate()),
	TrialPeriodExpired datetime null,
	DataSize float null,
	[State] nvarchar(128) null,
	UserSince datetime null,
	LastPaymentDate datetime null,
	[Locale] nvarchar(32) not null constraint DF_Tenants_Locale default(N'uk-UA')
);
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.SEQUENCES where SEQUENCE_SCHEMA=N'a2security' and SEQUENCE_NAME=N'SQ_Users')
	create sequence a2security.SQ_Users as bigint start with 100 increment by 1;
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2security' and TABLE_NAME=N'Users')
create table a2security.Users
(
	Id	bigint not null
		constraint DF_Users_PK default(next value for a2security.SQ_Users),
	Tenant int not null 
		constraint FK_Users_Tenant_Tenants foreign key references a2security.Tenants(Id),
	UserName nvarchar(255) not null constraint UNQ_Users_UserName unique,
	DomainUser nvarchar(255) null,
	Void bit not null constraint DF_Users_Void default(0),
	SecurityStamp nvarchar(max) not null,
	PasswordHash nvarchar(max) null,
	/*for .net core compatibility*/
	SecurityStamp2 nvarchar(max) null,
	PasswordHash2 nvarchar(max) null,
	TwoFactorEnabled bit not null constraint DF_Users_TwoFactorEnabled default(0),
	Email nvarchar(255) null,
	EmailConfirmed bit not null constraint DF_Users_EmailConfirmed default(0),
	PhoneNumber nvarchar(255) null,
	PhoneNumberConfirmed bit not null constraint DF_Users_PhoneNumberConfirmed default(0),
	LockoutEnabled	bit	not null constraint DF_Users_LockoutEnabled default(1),
	LockoutEndDateUtc datetimeoffset null,
	AccessFailedCount int not null constraint DF_Users_AccessFailedCount default(0),
	[Locale] nvarchar(32) not null constraint DF_Users_Locale2 default(N'uk-UA'),
	PersonName nvarchar(255) null,
	LastLoginDate datetime null, /*UTC*/
	LastLoginHost nvarchar(255) null,
	Memo nvarchar(255) null,
	ChangePasswordEnabled bit not null constraint DF_Users_ChangePasswordEnabled default(1),
	RegisterHost nvarchar(255) null,
	Segment nvarchar(32) null,
	SetPassword bit,
	UtcDateCreated datetime not null constraint DF_Users_UtcDateCreated default(getutcdate()),
	constraint PK_Users primary key (Tenant, Id)
);
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2security' and TABLE_NAME=N'ApiUserLogins')
create table a2security.ApiUserLogins
(
	[User] bigint not null,
	Tenant int not null, 
	[Mode] nvarchar(16) not null, -- ApiKey, OAuth2, JWT
	[ClientId] nvarchar(255),
	[ClientSecret] nvarchar(255),
	[ApiKey] nvarchar(255),
	[AllowIP] nvarchar(1024),
	Memo nvarchar(255),
	RedirectUrl nvarchar(255),
	[UtcDateModified] datetime not null constraint DF_ApiUserLogins_DateModified default(getutcdate()),
	constraint PK_ApiUserLogins primary key clustered ([User], Tenant, Mode) with (fillfactor = 70),
	constraint FK_ApiUserLogins_User_Users foreign key (Tenant, [User]) references a2security.Users(Tenant, Id)

);
go
------------------------------------------------
create or alter view a2security.ViewUsers
as
	select Id, UserName, DomainUser, PasswordHash, SecurityStamp, Email, PhoneNumber,
		LockoutEnabled, AccessFailedCount, LockoutEndDateUtc, TwoFactorEnabled, [Locale],
		PersonName, Memo, Void, LastLoginDate, LastLoginHost, Tenant, EmailConfirmed,
		PhoneNumberConfirmed, RegisterHost, ChangePasswordEnabled, Segment,
		SecurityStamp2, PasswordHash2, SetPassword
	from a2security.Users u
	where Void = 0 and Id <> 0;
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2ui' and TABLE_NAME=N'Modules')
create table a2ui.Modules
(
	Id uniqueidentifier not null,
	Parent uniqueidentifier null,
	[Name] nvarchar(255),
	[Memo] nvarchar(255),
	constraint PK_Modules primary key (Id),
	constraint FK_Modules_Parent_Modules foreign key (Parent) references a2ui.Modules(Id)
);
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2ui' and TABLE_NAME=N'TenantModules')
create table a2ui.TenantModules
(
	Tenant int not null,
	Module uniqueidentifier not null,
	UtcDateCreated datetime not null constraint 
		DF_TenantModules_UtcDateCreated default(getutcdate()),
	constraint PK_TenantModules primary key (Tenant, Module),
	constraint FK_TenantModules_Tenant_Tenants foreign key (Tenant) references a2security.Tenants(Id),
	constraint FK_TenantModules_Module_Modules foreign key (Module) references a2ui.Modules(Id)
);
go
-------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2ui' and TABLE_NAME=N'ModuleInitProcedures')
create table a2ui.[ModuleInitProcedures]
(
	[Procedure] sysname,
	Module  uniqueidentifier not null,
	Memo nvarchar(255),
	constraint PK_ModuleInitProcedures primary key (Module, [Procedure]),
	constraint FK_ModuleInitProcedures_Module_Modules foreign key (Module) references a2ui.Modules(Id)
);
go
-------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2ui' and TABLE_NAME=N'TenantInitProcedures')
create table a2ui.[TenantInitProcedures]
(
	[Procedure] sysname,
	Module  uniqueidentifier not null,
	Memo nvarchar(255),
	constraint PK_TenantInitProcedures primary key (Module, [Procedure]),
	constraint FK_TenantInitProcedures_Module_Modules foreign key (Module) references a2ui.Modules(Id)
);
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.TABLES where TABLE_SCHEMA=N'a2ui' and TABLE_NAME=N'Menu')
create table a2ui.Menu
(
	Tenant int not null,
	Id uniqueidentifier not null,
	Module uniqueidentifier not null,
	Parent uniqueidentifier,
	[Name] nvarchar(255),
	[Url] nvarchar(255),
	CreateName nvarchar(255),
	CreateUrl nvarchar(255),
	Icon nvarchar(255),
	[Order] int not null constraint DF_Menu_Order default(0),
	[ClassName] nvarchar(255) null,
	constraint PK_Menu primary key (Tenant, Id),
	constraint FK_Menu_Parent_Menu foreign key (Tenant, Parent) references a2ui.Menu(Tenant, Id),
	constraint FK_Menu_Module_Modules foreign key (Module) references a2ui.Modules(Id),
	constraint FK_Menu_Tenant_Module_TenantModules foreign key (Tenant, Module) references a2ui.TenantModules(Tenant, Module)
);
go

/*
Copyright © 2008-2023 Oleksandr Kukhtin

Last updated : 24 may 2023
module version : 8100
*/
------------------------------------------------
create or alter procedure a2sys.[AppTitle.Load]
as
begin
	set nocount on;
	select [AppTitle], [AppSubTitle]
	from (select [Name], [Value] = StringValue from a2sys.SysParams) as s
		pivot (min(Value) for [Name] in ([AppTitle], [AppSubTitle])) as p;
end
go

------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.DOMAINS where DOMAIN_SCHEMA=N'a2sys' and DOMAIN_NAME=N'Id.TableType' and DATA_TYPE=N'table type')
create type a2sys.[Id.TableType] as table(
	Id bigint null
);
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.DOMAINS where DOMAIN_SCHEMA=N'a2sys' and DOMAIN_NAME=N'GUID.TableType' and DATA_TYPE=N'table type')
create type a2sys.[GUID.TableType] as table(
	Id uniqueidentifier null
);
go
------------------------------------------------
if not exists(select * from INFORMATION_SCHEMA.DOMAINS where DOMAIN_SCHEMA=N'a2sys' and DOMAIN_NAME=N'NameValue.TableType' and DATA_TYPE=N'table type')
create type a2sys.[NameValue.TableType] as table(
	[Name] nvarchar(255),
	[Value] nvarchar(max)
);
go


/*
Copyright © 2008-2023 Oleksandr Kukhtin

Last updated : 03 jul 2023
module version : 8110
*/
-- SECURITY
------------------------------------------------
create or alter procedure a2security.FindUserById
@Id bigint
as
begin
	set nocount on;
	set transaction isolation level read uncommitted;

	select * from a2security.ViewUsers where Id=@Id;
end
go
------------------------------------------------
create or alter procedure a2security.FindUserByEmail
@Email nvarchar(255)
as
begin
	set nocount on;
	set transaction isolation level read uncommitted;

	select * from a2security.ViewUsers where Email=@Email;
end
go
------------------------------------------------
create or alter procedure a2security.FindUserByName
@UserName nvarchar(255)
as
begin
	set nocount on;
	set transaction isolation level read uncommitted;

	select * from a2security.ViewUsers where UserName=@UserName;
end
go
------------------------------------------------
create or alter procedure a2security.FindUserByPhoneNumber
@PhoneNumber nvarchar(255)
as
begin
	set nocount on;
	set transaction isolation level read uncommitted;

	select * from a2security.ViewUsers where PhoneNumber=@PhoneNumber;
end
go
------------------------------------------------
create or alter procedure a2security.UpdateUserLogin
@Id bigint,
@LastLoginDate datetime,
@LastLoginHost nvarchar(255)
as
begin
	set nocount on;
	set transaction isolation level read committed;
	update a2security.ViewUsers set LastLoginDate = @LastLoginDate, LastLoginHost = @LastLoginHost 
	where Id=@Id;
end
go
------------------------------------------------
create or alter procedure a2security.[User.SetAccessFailedCount]
@Id bigint,
@AccessFailedCount int
as
begin
	set nocount on;
	set transaction isolation level read committed;

	update a2security.ViewUsers set AccessFailedCount = @AccessFailedCount
	where Id=@Id;
end
go
------------------------------------------------
create or alter procedure a2security.[User.SetLockoutEndDate]
@Id bigint,
@LockoutEndDate datetimeoffset
as
begin
	set nocount on;
	set transaction isolation level read committed;

	update a2security.ViewUsers set LockoutEndDateUtc = @LockoutEndDate where Id=@Id;
end
go

------------------------------------------------
create or alter procedure a2security.[User.SetPasswordHash]
@Id bigint,
@PasswordHash nvarchar(max)
as
begin
	set nocount on;
	set transaction isolation level read committed;

	update a2security.ViewUsers set PasswordHash2 = @PasswordHash where Id = @Id;
end
go
------------------------------------------------
create or alter procedure a2security.[User.SetSecurityStamp]
@Id bigint,
@SecurityStamp nvarchar(max)
as
begin
	set nocount on;
	set transaction isolation level read committed;

	update a2security.ViewUsers set SecurityStamp2 = @SecurityStamp where Id = @Id;
end
go
------------------------------------------------
create or alter procedure a2security.[User.SetPhoneNumberConfirmed]
@Id bigint,
@PhoneNumber nvarchar(255),
@Confirmed bit
as
begin
	set nocount on;
	set transaction isolation level read committed;
	update a2security.ViewUsers set PhoneNumber = @PhoneNumber, PhoneNumberConfirmed = @Confirmed where Id = @Id;
end
go
------------------------------------------------
create or alter procedure a2security.FindApiUserByApiKey
@Host nvarchar(255) = null,
@ApiKey nvarchar(255) = null
as
begin
	set nocount on;
	set transaction isolation level read committed;

	declare @status nvarchar(255);
	declare @code int;

	set @status = N'ApiKey=' + @ApiKey;
	set @code = 65; /*fail*/

	declare @user table(Id bigint, Tenant int, Segment nvarchar(255), [Name] nvarchar(255), ClientId nvarchar(255), AllowIP nvarchar(255));
	insert into @user(Id, Tenant, Segment, [Name], ClientId, AllowIP)
	select top(1) u.Id, u.Tenant, Segment, [Name]=u.UserName, s.ClientId, s.AllowIP 
	from a2security.Users u inner join a2security.ApiUserLogins s on u.Id = s.[User] and u.Tenant = s.Tenant
	where u.Void=0 and s.Mode = N'ApiKey' and s.ApiKey=@ApiKey;
	
	if @@rowcount > 0 
	begin
		set @code = 64 /*sucess*/;
		update a2security.Users set LastLoginDate=getutcdate(), LastLoginHost=@Host
		from @user t inner join a2security.Users u on t.Id = u.Id;
	end

	--insert into a2security.[Log] (UserId, Severity, Code, Host, [Message])
		--values (0, N'I', @code, @Host, @status);

	select * from @user;
end
go
------------------------------------------------
create or alter function a2security.fn_GetCurrentSegment()
returns nvarchar(32)
as
begin
	declare @ret nvarchar(32);
	select @ret = null;
	return @ret;
end
go

------------------------------------------------
create or alter procedure a2security.CreateUser 
@Tenant int = 1, -- default value
@UserName nvarchar(255),
@PasswordHash nvarchar(max) = null,
@SecurityStamp nvarchar(max),
@Email nvarchar(255) = null,
@PhoneNumber nvarchar(255) = null,
@PersonName nvarchar(255) = null,
@Memo nvarchar(255) = null,
@Locale nvarchar(255) = null,
@RetId bigint output
as
begin
	set nocount on;
	set transaction isolation level read committed;
	set xact_abort on;

	set @Tenant = isnull(@Tenant, 1); -- default value
	set @Locale = isnull(@Locale, N'uk-UA');

	declare @tenants table(Id int);
	declare @users table(Id bigint);
	declare @userId bigint;
	declare @tenantCreated bit = 0;

	begin tran;

	if @Tenant = -1
	begin
		insert into a2security.Tenants ([Admin], Locale)
		output inserted.Id into @tenants(Id)
		values (null, @Locale);
		select top(1) @Tenant = Id from @tenants;
		set @tenantCreated = 1;
	end

	insert into a2security.Users(Tenant, UserName, PersonName, Email, PhoneNumber, SecurityStamp, PasswordHash,
		Segment, Locale, Memo)
	output inserted.Id into @users(Id)
	values (@Tenant, @UserName, @PersonName, @Email, @PhoneNumber, @SecurityStamp, @PasswordHash, 
		a2security.fn_GetCurrentSegment(), @Locale, @Memo);
	select top(1) @userId = Id from @users;

	if @tenantCreated = 1
		update a2security.Tenants set [Admin] = @userId where Id = @Tenant;
	set @RetId = @userId;
	commit tran;
end
go
------------------------------------------------
create or alter procedure a2security.[User.RegisterComplete]
@Id bigint
as
begin
	set nocount on;
	set transaction isolation level read uncommitted;

	update a2security.Users set EmailConfirmed = 1, LastLoginDate = getutcdate()

	select * from a2security.ViewUsers where Id=@Id;
end
go
