I use [a log file](https://support.microsoft.com/en-us/topic/how-to-use-notepad-to-create-a-log-file-dd228763-76de-a7a7-952b-d5ae203c4e12) to track billable time.
Its format is as follows:

	.LOG

	11:31 27/11/2018
	started
	11:49 27/11/2018
	email "Is JavaScript arithmetic deterministic?"

	13:05 27/11/2018
	started
	13:25 27/11/2018
	email "securitised-options unit test failed"
	13:29 27/11/2018
	run integration tests on securitised-options branch

	20:39 27/11/2018
	started
	23:08 27/11/2018
	begin to write LANGUAGE.md
	investigate the API for reading transactions and/or tokens

This `TimeLog` program parses this type of log file as its input, and it creates two output files --
i.e. a cleaner-looking `*.details.txt` file like this ...

	LOGGED

	11:31 2018-11-27
	email "Is JavaScript arithmetic deterministic?"
	11:49 2018-11-27

	13:05 2018-11-27
	email "securitised-options unit test failed"
	run integration tests on securitised-options branch
	13:29 2018-11-27

	20:39 2018-11-27
	begin to write LANGUAGE.md
	investigate the API for reading transactions and/or tokens
	23:08 2018-11-27

	3

... and a `*.summary.txt` file like this ...

	2018-11-27	3

... which I use for invoicing based on when and how I worked.

The program is written for personal use, and the parser is brittle, i.e.
it throws an exception if the input file has any unexpected syntax. :-)
