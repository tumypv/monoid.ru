CREATE DATABASE IF NOT EXISTS `fsharp`
USE `fsharp`;

CREATE TABLE IF NOT EXISTS `chapter` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `title` varchar(160) COLLATE utf8mb4_unicode_ci NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=5 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `check_solution_begin`()
    SQL SECURITY INVOKER
BEGIN
    update
        solution
    set
        state = 'checking'
    where
        state = 'submitted'
        and id = LAST_INSERT_ID(id)
    order by
        id
    limit 1;

    select 
        (
            select
                JSON_OBJECT(
                    'id', s.id,
                    'source', s.source,
                    'time', p.time_limit,
                    'memory', p.memory_limit,
                    'tests', JSON_ARRAYAGG(
                        JSON_OBJECT('id', id, 'input', input, 'output', output)
                )) result
            from test t
            where t.problem = s.problem
        ) tests
    from solution s 
    join problem p on p.id = s.problem
    where s.id = LAST_INSERT_ID();
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `check_solution_end`(IN `solution` INT, IN `error` VARCHAR(250)



, IN `token_count` INT, IN `literal_length` INT)
    SQL SECURITY INVOKER
BEGIN
    update
        solution s
    set
        s.state = 'checked',
        s.compilation_error = error,
        s.token_count = token_count,
        s.literal_length = literal_length
    where
        s.id = solution;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `createGitHubUser`(
	IN `login` VARCHAR(50),
	IN `name` VARCHAR(50)
)
    MODIFIES SQL DATA
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
    declare u int;
    set u = (select user from user_github g where g.login = login);
    if u is null then
        insert into user (full_name) values (name);
        set u = (LAST_INSERT_ID());
        insert into user_github (login, name, user) values (login, name, u);
    end if;
    select u;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `createProblem`(
	IN `title` VARCHAR(150),
	IN `content` JSON
)
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
    insert into problem (title, content) values (title, content);
    SELECT LAST_INSERT_ID();
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `createVkUser`(
	IN `id` int(11),
	IN `first_name` VARCHAR(50),
	IN `last_name` VARCHAR(50)
)
    MODIFIES SQL DATA
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
    declare u int;
    set u = (select user from user_vk v where v.id = id);
    if u is null then
        insert into user (full_name) values (concat(first_name, ' ', last_name));
        set u = (LAST_INSERT_ID());
        insert into user_vk (id, first_name, last_name, user) values (id, first_name, last_name, u);
    end if;
    select u;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `get_hall_of_fame`()
    READS SQL DATA
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
  select 
  		JSON_ARRAYAGG(JSON_OBJECT('user', user, 'problem', problem, 'conciseness', JSON_OBJECT('tokenCount', token_count, 'literalLength', literal_length))) 
  from
      solution_summary ss
      join user u on ss.user = u.id
  where
      verdict = 'accepted' and u.hidden = 0;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `get_problems`()
    READS SQL DATA
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
    select
        JSON_ARRAYAGG(
            JSON_OBJECT(
                'id', id,
                'title', title
            )
        )
    from
        problem
    order by id;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `get_solution`(IN `solution_id` INT

)
    READS SQL DATA
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
	select
		JSON_OBJECT(
			'summary',
			JSON_OBJECT(
				'user', ss.user,
				'solution', ss.id,
				'problem', ss.problem,
				'submitDate', DATE_FORMAT(ss.submitDate, '%Y-%m-%dT%T.000Z'),
				'verdict', ss.verdict, 
				'failedTest', ss.failedTest, 
				'error', ss.error,
				'memory', ss.memory,
                'conciseness',
                JSON_OBJECT(
                    'tokenCount', token_count,
                    'literalLength', literal_length
                )                
			),
			'source', (select source from solution where id = ss.id)
		) o
	from 
		solution_summary  ss
	where 
		ss.id = solution_id;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `get_solution_state`(
	IN `solution_id` INT
)
    READS SQL DATA
    SQL SECURITY INVOKER
BEGIN
	select state from solution where id = solution_id;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `get_users`()
    READS SQL DATA
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
    select JSON_ARRAYAGG(JSON_OBJECT('id', id, 'fullName', full_name)) from user where hidden = 0;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `get_user_solutions`(IN `user_id` INT)
    READS SQL DATA
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
    select
        JSON_ARRAYAGG(
            JSON_OBJECT(
                'user', s.user,
                'solution', s.id,
                'problem', s.problem, 
                'submitDate', DATE_FORMAT(s.server_time, '%Y-%m-%dT%T.000Z'),
                'error', coalesce(s.compilation_error, if(tr.result = 'runtime_error', tr.output, null)),
                'verdict', 
                coalesce(
                    if (state = 'checked', null, state),
                    if(s.compilation_error is null, null, 'compilation_error'), 
                    tr.result, 
                    'accepted'
                ),
                'failedTest', t.n,
                'memory', (select max(memory) from test_result tr where result = 'passed' and tr.solution = s.id),
                'conciseness',
                JSON_OBJECT(
                    'tokenCount', token_count,
                    'literalLength', literal_length
                )
            )
        ) r
    from
    solution s
    left join test_result tr on s.id = tr.solution and tr.result <> 'passed'
    left join test t on tr.test = t.id
    where s.user = user_id
    order by s.id;
END//
DELIMITER ;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `get_user_solutions_for_problem`(IN `user_id` INT, IN `problem_id` INT)
    SQL SECURITY INVOKER
BEGIN
select 
    JSON_ARRAYAGG(
        JSON_REMOVE(JSON_OBJECT(
            'user', s.user,
            'solution', s.id,
            'problem', s.problem, 
            'submitDate', DATE_FORMAT(s.submitDate, '%Y-%m-%dT%T.000Z'),
            'error', s.error,
            'verdict', s.verdict,
            'failedTest', s.failedTest,
            'memory', 0,
            'conciseness', JSON_OBJECT(
                'tokenCount', token_count,
                'literalLength', literal_length
            )
    ), if(s.failedTest is null, '$.failedTest', '$.dummy'))
) r
from 
	solution_summary s
where 
	s.user = user_id and s.problem = problem_id
order by s.id desc;

END//
DELIMITER ;

CREATE TABLE IF NOT EXISTS `problem` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `title` varchar(100) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '0',
  `content` json NOT NULL,
  `memory_limit` int(11) NOT NULL DEFAULT '20',
  `time_limit` int(11) NOT NULL DEFAULT '2000',
  `custom_checker` text COLLATE utf8mb4_unicode_ci,
  `chapter` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=34 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `save_test_result`(
	IN `solution` INT,
	IN `test` INT,
	IN `memory` INT,
	IN `result` VARCHAR(50),
	IN `output` VARCHAR(250)
)
    SQL SECURITY INVOKER
BEGIN
    insert into
        test_result (solution, test, memory, result, output)
    values 
        (solution, test, memory, result, output);
END//
DELIMITER ;

CREATE TABLE IF NOT EXISTS `solution` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `user` int(11) NOT NULL,
  `problem` int(11) NOT NULL,
  `server_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `source` text COLLATE utf8mb4_unicode_ci NOT NULL,
  `state` enum('submitted','checking','checked') COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT 'submitted',
  `compilation_error` varchar(250) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  `token_count` int(11) NOT NULL DEFAULT '0',
  `literal_length` int(11) NOT NULL DEFAULT '0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=440 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE `solution_summary` (
	`id` INT(11) NOT NULL,
	`user` INT(11) NOT NULL,
	`problem` INT(11) NOT NULL,
	`submitDate` DATETIME NOT NULL,
	`error` VARCHAR(250) NULL COLLATE 'utf8mb4_unicode_ci',
	`verdict` VARCHAR(21) NULL COLLATE 'utf8mb4_unicode_ci',
	`failedTest` INT(11) NULL,
	`memory` BIGINT(11) NULL,
	`token_count` INT(11) NOT NULL,
	`literal_length` INT(11) NOT NULL
) ENGINE=MyISAM;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `submit_solution`(
	IN `user` INT,
	IN `problem` INT,
	IN `source` TEXT

)
    MODIFIES SQL DATA
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
  insert into solution (`user`, `problem`, `source`) values (user, problem, source);
  select LAST_INSERT_ID();
END//
DELIMITER ;

CREATE TABLE IF NOT EXISTS `test` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `n` int(11) NOT NULL,
  `problem` int(11) NOT NULL,
  `is_example` bit(1) NOT NULL DEFAULT b'0',
  `input` text COLLATE utf8mb4_unicode_ci NOT NULL,
  `output` text COLLATE utf8mb4_unicode_ci NOT NULL,
  PRIMARY KEY (`id`),
  KEY `problem` (`problem`)
) ENGINE=InnoDB AUTO_INCREMENT=253 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `test_result` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `solution` int(11) NOT NULL,
  `test` int(11) NOT NULL,
  `start_time` datetime NOT NULL DEFAULT CURRENT_TIMESTAMP,
  `memory` int(11) NOT NULL,
  `result` enum('passed','wrong_answer','time_limit_exceeded','presentation_error','memory_limit_exceeded','runtime_error') COLLATE utf8mb4_unicode_ci NOT NULL,
  `output` varchar(250) COLLATE utf8mb4_unicode_ci DEFAULT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=3319 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DELIMITER //
CREATE DEFINER=`checker`@`%.khakasnet.ru` PROCEDURE `updateProblem`(
	IN `id` int(11),
	IN `title` VARCHAR(150),
	IN `content` JSON
)
    DETERMINISTIC
    SQL SECURITY INVOKER
BEGIN
    update problem p set p.title = title, p.content = content where p.id = id;
END//
DELIMITER ;

CREATE TABLE IF NOT EXISTS `user` (
  `id` int(11) NOT NULL AUTO_INCREMENT,
  `full_name` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
  `f` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '""',
  `i` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '""',
  `o` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL DEFAULT '""',
  `hidden` bit(1) NOT NULL DEFAULT b'0',
  PRIMARY KEY (`id`)
) ENGINE=InnoDB AUTO_INCREMENT=25 DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `user_github` (
  `login` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
  `name` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
  `user` int(11) NOT NULL,
  PRIMARY KEY (`login`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

CREATE TABLE IF NOT EXISTS `user_vk` (
  `id` int(11) NOT NULL,
  `first_name` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
  `last_name` varchar(50) COLLATE utf8mb4_unicode_ci NOT NULL,
  `user` int(11) NOT NULL,
  PRIMARY KEY (`id`)
) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;

DROP TABLE IF EXISTS `solution_summary`;
CREATE ALGORITHM=UNDEFINED DEFINER=`checker`@`%.khakasnet.ru` SQL SECURITY DEFINER VIEW `solution_summary` AS select `s`.`id` AS `id`,`s`.`user` AS `user`,`s`.`problem` AS `problem`,`s`.`server_time` AS `submitDate`,coalesce(`s`.`compilation_error`,if((`tr`.`result` = 'runtime_error'),`tr`.`output`,NULL)) AS `error`,coalesce(if((`s`.`state` = 'checked'),NULL,`s`.`state`),if(isnull(`s`.`compilation_error`),NULL,'compilation_error'),`tr`.`result`,'accepted') AS `verdict`,`t`.`n` AS `failedTest`,(select max(`tr`.`memory`) from `test_result` `tr` where ((`tr`.`result` = 'passed') and (`tr`.`solution` = `s`.`id`))) AS `memory`,`s`.`token_count` AS `token_count`,`s`.`literal_length` AS `literal_length` from ((`solution` `s` left join `test_result` `tr` on(((`s`.`id` = `tr`.`solution`) and (`tr`.`result` <> 'passed')))) left join `test` `t` on((`tr`.`test` = `t`.`id`)));